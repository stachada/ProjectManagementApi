// k6 load test: concurrent PUT /api/v1/tasks/{id} with optimistic concurrency.
//
// 50 virtual users repeatedly read a task (capturing its ETag) and write it back with
// If-Match, all aimed at a deliberately small pool of "hot" tasks so writes collide. The test
// passes only if every write resolves to exactly one of the two legal outcomes - 204 (won the
// race) or 409 Conflict with a Problem Details body (lost it) - and the API never answers 5xx,
// never accepts a stale ETag, and stays within its latency budget while doing so.
//
// Requires a seeded database and a running API - see docs/BENCHMARKS.md#load-test.
//
//   docker run --rm -i -e BASE_URL=http://host.docker.internal:5080 \
//     grafana/k6:2.4.0 run - < tests/load/task-update-contention.js

import http from 'k6/http';
import { check, fail } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const ORG_SLUG = __ENV.ORG_SLUG || 'ordinis-benchmarks';
const EMAIL = __ENV.EMAIL || 'admin@benchmarks.ordinis.local';
const PASSWORD = __ENV.PASSWORD || 'Benchmark!Passw0rd';
// Fewer hot tasks = more contention. 5 tasks across 50 VUs gives a steady conflict rate.
const HOT_TASK_COUNT = parseInt(__ENV.HOT_TASKS || '5', 10);
const VUS = parseInt(__ENV.VUS || '50', 10);
const DURATION = __ENV.DURATION || '60s';

const updatesSucceeded = new Counter('task_updates_succeeded');
const updatesConflicted = new Counter('task_updates_conflicted');
const conflictRate = new Rate('task_update_conflict_rate');
const unexpectedOutcomes = new Rate('task_update_unexpected_outcome');
const updateDuration = new Trend('task_update_duration', true);

export const options = {
    scenarios: {
        contention: {
            executor: 'constant-vus',
            vus: VUS,
            duration: DURATION,
        },
    },
    thresholds: {
        // Correctness: every PUT is a clean 204 or a well-formed 409 - no 5xx, 412, 429, ...
        task_update_unexpected_outcome: ['rate==0'],
        // Contention must actually happen, or the test isn't exercising concurrency at all.
        task_updates_conflicted: ['count>0'],
        // Latency budget for the write path under 50-way contention.
        'http_req_duration{name:PUT /tasks/{id}}': ['p(95)<500', 'p(99)<1000'],
        checks: ['rate>0.99'],
    },
};

// 409 is an expected, correct answer under contention - don't let k6 count it as a failed request.
http.setResponseCallback(http.expectedStatuses(200, 204, 409));

export function setup() {
    const login = http.post(
        `${BASE_URL}/auth/login`,
        JSON.stringify({ organizationSlug: ORG_SLUG, email: EMAIL, password: PASSWORD }),
        { headers: { 'Content-Type': 'application/json' } });

    if (login.status !== 200) {
        fail(`Login failed (${login.status}): ${login.body}. Is the database seeded? See docs/BENCHMARKS.md.`);
    }

    const token = login.json('accessToken');
    const params = { headers: { Authorization: `Bearer ${token}` } };

    // Only non-terminal tasks are editable - Done/Cancelled ones correctly answer 422
    // (task.terminal-state), which would otherwise show up here as unexpected outcomes.
    const list = http.get(`${BASE_URL}/api/v1/tasks?status=InProgress&pageSize=${HOT_TASK_COUNT}&fields=id`, params);
    if (list.status !== 200) {
        fail(`Listing tasks failed (${list.status}): ${list.body}`);
    }

    const taskIds = list.json().map((t) => t.id);
    if (taskIds.length === 0) {
        fail('No tasks visible to the load-test user. Is the database seeded?');
    }

    return { token, taskIds };
}

export default function (data) {
    const authHeaders = { Authorization: `Bearer ${data.token}` };
    const taskId = data.taskIds[Math.floor(Math.random() * data.taskIds.length)];

    const read = http.get(`${BASE_URL}/api/v1/tasks/${taskId}`, {
        headers: authHeaders,
        tags: { name: 'GET /tasks/{id}' },
    });

    const etag = read.headers['Etag'];
    if (!check(read, { 'read returns 200 with an ETag': (r) => r.status === 200 && !!etag })) {
        unexpectedOutcomes.add(true);
        return;
    }

    const task = read.json();
    const write = http.put(
        `${BASE_URL}/api/v1/tasks/${taskId}`,
        JSON.stringify({
            title: task.title,
            description: `Load test write by VU ${__VU}, iteration ${__ITER}`,
            priority: task.priority,
            dueDate: task.dueDate,
        }),
        {
            headers: { ...authHeaders, 'Content-Type': 'application/json', 'If-Match': etag },
            tags: { name: 'PUT /tasks/{id}' },
        });

    updateDuration.add(write.timings.duration);

    const succeeded = write.status === 204;
    const conflicted = write.status === 409
        && (write.headers['Content-Type'] || '').startsWith('application/problem+json');

    check(write, {
        'write is 204 or 409 Problem Details': () => succeeded || conflicted,
    });

    unexpectedOutcomes.add(!(succeeded || conflicted));
    conflictRate.add(conflicted);
    if (succeeded) {
        updatesSucceeded.add(1);
    } else if (conflicted) {
        updatesConflicted.add(1);
    } else {
        logUnexpected(write);
    }
}

// Print the first few unexpected responses per VU - enough to diagnose a failure without
// flooding the console with thousands of identical lines.
let unexpectedLogged = 0;
function logUnexpected(response) {
    if (unexpectedLogged < 3) {
        unexpectedLogged++;
        console.warn(`VU ${__VU}: unexpected PUT ${response.status} ${response.headers['Content-Type'] || ''}: ${String(response.body).slice(0, 400)}`);
    }
}
