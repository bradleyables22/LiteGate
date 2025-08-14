namespace Server.Utiilites
{
    public class TryResult<T>
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Success { get; set; }
        public T? Data { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string? Message { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Exception? Exception { get; set; }

        public static TryResult<T> Pass(T data)
        {
            return new TryResult<T>
            {
                Success = true,
                Data = data,
                Message = null,
                Exception = default
            };
        }

        public static TryResult<T> Fail(string message, Exception exception)
        {
            return new TryResult<T>
            {
                Success = false,
                Data = default,
                Message = message,
                Exception = exception
            };
        }

        public IResult ToResult(bool asText = false)
        {
            if (Success)
            {
                if (Data is not null) 
                    return asText ? Results.Text(this.Data.ToString()) : Results.Ok(this.Data); 
                
                return Results.NoContent();
            }

            return Results.Problem(
                detail: Message,
                title: "Error",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
