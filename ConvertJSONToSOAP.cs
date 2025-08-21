using FunctionApp;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Xml;

namespace ConvertSoapToJson
{
    public class ConvertJSONToSOAP
    {
        private readonly ILogger<ConvertJSONToSOAP> _logger;

        public ConvertJSONToSOAP(ILogger<ConvertJSONToSOAP> logger)
        {
            _logger = logger;
        }

        [Function("ConvertJSONToSOAP")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            FunctionContext context)
        {
            try
            {
                string json = await new StreamReader(req.Body).ReadToEndAsync();

                XmlDocument soapEnvelope = new XmlDocument();
                XmlDeclaration xmlDeclaration = soapEnvelope.CreateXmlDeclaration("1.0", "utf-8", null);
                soapEnvelope.AppendChild(xmlDeclaration);

                XmlElement envelope = soapEnvelope.CreateElement("soap", "Envelope", APIHelper.soapenv);
                envelope.SetAttribute("xmlns:xsi", APIHelper.xsi);
                envelope.SetAttribute("xmlns:xsd", APIHelper.xsd);
                soapEnvelope.AppendChild(envelope);

                XmlElement body = soapEnvelope.CreateElement("soap", "Body", APIHelper.soapenv);
                envelope.AppendChild(body);

                XmlElement addResponse = soapEnvelope.CreateElement("AddResponse", APIHelper.tempuri);
                body.AppendChild(addResponse);

                XmlElement addResult = soapEnvelope.CreateElement("AddResult", APIHelper.tempuri);
                addResponse.AppendChild(addResult);

                using JsonDocument jsonDoc = JsonDocument.Parse(json);
                XmlElement rootElement = ConvertJsonToXml(soapEnvelope, jsonDoc.RootElement);
                addResult.AppendChild(rootElement);

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteStringAsync(soapEnvelope.OuterXml);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse SOAP request.");
                var response = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await response.WriteStringAsync("Error: " + ex.Message);
                return response;
            }
        }

        static XmlElement ConvertJsonToXml(XmlDocument doc, JsonElement jsonElement)
        {
            XmlElement root = doc.CreateElement("root");

            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in jsonElement.EnumerateArray())
                {
                    root.AppendChild(CreateItemElement(doc, item));
                }
            }
            else if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                root.AppendChild(CreateItemElement(doc, jsonElement));
            }
            else
            {
                throw new ArgumentException("JSON root must be an object or array");
            }

            return root;
        }

        static XmlElement CreateItemElement(XmlDocument doc, JsonElement item)
        {
            XmlElement itemElement = doc.CreateElement("item");

            foreach (var property in item.EnumerateObject())
            {
                XmlElement propertyElement = doc.CreateElement(property.Name);

                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    propertyElement.SetAttribute("nil", APIHelper.xsi, "true");
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var arrayItem in property.Value.EnumerateArray())
                    {
                        XmlElement arrayElement = doc.CreateElement("message");
                        arrayElement.InnerText = arrayItem.ToString();
                        propertyElement.AppendChild(arrayElement);
                    }
                }
                else
                {
                    propertyElement.InnerText = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                }

                itemElement.AppendChild(propertyElement);
            }

            return itemElement;
        }
    }
}