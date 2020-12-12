using DocXToPDFConverterContracts.Models;
using System.Collections.Generic;

namespace DocXToPDFConverterContracts.Messages
{
    public class FileListConversionResponse
    {
        public IEnumerable<FileConversionResponseEntry> Files { get; set; }
    }
}
