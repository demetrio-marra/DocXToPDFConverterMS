namespace DocXToPDFConverterContracts.Messages
{
    public class FileConversionResponse
    {
        public string SourceFileFullPath { get; set; }
        public string ConvertedFileFullPath { get; set; }
        public bool IsError { get; set; }
        public string ErrorDescription { get; set; }
    }
}
