namespace OsService.Domain.Enums;

public enum ServiceOrderStatus
{
    Received = 1,
    InDiagnosis = 2,
    WaitingApproval = 3,
    InExecution = 4,
    Finished = 5,
    Delivered = 6,
    Canceled = 7
}
