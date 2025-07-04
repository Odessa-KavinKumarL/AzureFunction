using System.IO;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MyFunctionApp.Services;

namespace MyFunctionApp.Functions
{
    public class ICSFunction
    {
        private readonly ILogger _logger;
        private readonly ICSResponseProcessor _processor;

        public ICSFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ICSFunction>();
            _processor = new ICSResponseProcessor();
        }

        [Function("SubmitICSResponse")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            string body = await new StreamReader(req.Body).ReadToEndAsync();

            var processor = new ICSResponseProcessor();
            try
            {
                req.Headers.Clear();

                var temo = await processor.ProcessICSResponse(body, req);
                return temo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process ICS XML");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Internal Server Error");
                return errorResponse;
            }
        }
    }
}
