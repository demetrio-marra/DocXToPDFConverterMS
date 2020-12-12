using Newtonsoft.Json;
using System.Text;

namespace DocXToPDFConverterUtils
{
    public class SerializeUtils
    {
        public static byte[] RabbitSerialize(object o) =>
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o));

        public static T RabbitDeserialize<T>(byte[] b) =>
            JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(b));
    }
}
