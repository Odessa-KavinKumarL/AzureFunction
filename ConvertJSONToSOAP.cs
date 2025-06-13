using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
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
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            try
            {

                string json = await new StreamReader(req.Body).ReadToEndAsync();

                XmlDocument soapEnvelope = new XmlDocument();

                XmlDeclaration xmlDeclaration = soapEnvelope.CreateXmlDeclaration("1.0", "utf-8", null);
                soapEnvelope.AppendChild(xmlDeclaration);

                XmlElement envelope = soapEnvelope.CreateElement("soap", "Envelope", "http://schemas.xmlsoap.org/soap/envelope/");
                envelope.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
                envelope.SetAttribute("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");
                soapEnvelope.AppendChild(envelope);

                XmlElement body = soapEnvelope.CreateElement("soap", "Body", "http://schemas.xmlsoap.org/soap/envelope/");
                envelope.AppendChild(body);

                XmlElement addResponse = soapEnvelope.CreateElement("AddResponse", "http://tempuri.org/");
                body.AppendChild(addResponse);

                XmlElement addResult = soapEnvelope.CreateElement("AddResult", "http://tempuri.org/");
                addResponse.AppendChild(addResult);

                using JsonDocument jsonDoc = JsonDocument.Parse(json);
                XmlElement rootElement = ConvertJsonToXml(soapEnvelope, jsonDoc.RootElement);
                addResult.AppendChild(rootElement);

                return new OkObjectResult(soapEnvelope.OuterXml);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to parse SOAP request.");
                return new StatusCodeResult(500);
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
                    propertyElement.SetAttribute("nil", "http://www.w3.org/2001/XMLSchema-instance", "true");
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
