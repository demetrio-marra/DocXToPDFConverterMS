namespace DocXToPDFConverterContracts.Messages
{
    public class FileConversionRequest
    {
        public string SourceFileFullPath { get; set; }
        public string ConvertedFileFullPath { get; set; }
    }
}
