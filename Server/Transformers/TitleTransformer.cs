using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Server.Transformers
{
    public class TitleTransformer : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {

            document.Info.Title = $"LiteGate SQlite API";
            
            return Task.FromResult(document);
        }
    }
}
