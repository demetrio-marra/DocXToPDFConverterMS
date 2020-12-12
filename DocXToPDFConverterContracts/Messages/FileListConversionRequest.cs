using DocXToPDFConverterContracts.Models;
using System.Collections.Generic;

namespace DocXToPDFConverterContracts.Messages
{
    public class FileListConversionRequest
    {
        public IEnumerable<FileConversionRequestEntry> Files { get; set; }
    }
}
