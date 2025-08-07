namespace Server.Utiilites
{
    public class TryResult<T>
    {
        #region Fields

        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Exception? Exception { get; set; }

        #endregion

        #region Methods

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

        #endregion
    }
}
