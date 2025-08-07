namespace Server.Utiilites
{
    public class OffsetTryResult<T>
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Success { get; set; }
        public List<T>? Data { get; set; }
        public int ItemsCount
        {
            get
            {
                if (Data is not null && Data.Any())
                    return Data.Count();
                return 0;
            }
        }
        public int TotalCount { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string? Message { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public Exception? Exception { get; set; }

        public static OffsetTryResult<T> Pass(int total, List<T>? data)
        {
            return new OffsetTryResult<T>
            {
                Success = true,
                Data = data,
                TotalCount = total,
                Message = null,
                Exception = null
            };
        }

        public static OffsetTryResult<T> Fail(string message, Exception exception)
        {
            return new OffsetTryResult<T>
            {
                Success = false,
                Message = message,
                Exception = exception
            };
        }
        public IResult ToResult()
        {
            if (Success)
            {
                if (Data is not null && Data.Any())
                    return Results.Ok(this);

                return Results.NoContent();
            }

            return Results.Problem(
                detail: Message,
                title:"Error",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
