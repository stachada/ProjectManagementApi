namespace Ordinis.Api.Projects.Requests;

public sealed record CreateBoardRequest(
    Guid CreatedByUserId,
    string Name);
