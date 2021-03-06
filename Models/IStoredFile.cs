using Newtonsoft.Json.Serialization;
using System.Threading.Tasks;
namespace Homiebot.Models
{
    public interface IStoredFile
    {
        public string Name {get;set;}
        public string Identifier {get;set;}
        public string TypeName{get;set;}
        public Task<byte[]> LoadBytesAsync();
    }
}