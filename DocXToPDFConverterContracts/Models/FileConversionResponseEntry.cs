namespace DocXToPDFConverterContracts.Models
{
    public class FileConversionResponseEntry
    {
        public string SourceFileFullPath { get; set; }
        public string ConvertedFileFullPath { get; set; }
        public bool IsError { get; set; }
        public string ErrorDescription { get; set; }
    }
}
