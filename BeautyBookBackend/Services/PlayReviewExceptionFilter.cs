using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BeautyBookBackend.Services;

public sealed class PlayReviewExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not PlayReviewOperationException) return;
        context.Result = new ObjectResult(new {
            Code = "PLAY_REVIEW_OPERATION_BLOCKED",
            Message = "This operation is unavailable for the current account or resource domain."
        }) { StatusCode = 403 };
        context.ExceptionHandled = true;
    }
}
