namespace CapstoneServices.Galaxy
{
    /// <summary>
    /// Interface for reporting progress during long-running Galaxy operations
    /// </summary>
    public interface IProgressReporter
    {
        /// <summary>
        /// Report progress for a specific step
        /// </summary>
        /// <param name="step">Name of the current step (e.g., "Uploading", "Unzipping", "Running OrthoFinder")</param>
        /// <param name="message">Detailed message about what's happening</param>
        /// <param name="current">Current progress value (e.g., number of completed items)</param>
        /// <param name="total">Total number of items (e.g., total species)</param>
        /// <param name="details">Additional details (e.g., species name, file name)</param>
        void ReportProgress(string step, string message, int? current = null, int? total = null, object? details = null);
    }
    
    /// <summary>
    /// No-op progress reporter for when progress tracking is not needed
    /// </summary>
    public sealed class NullProgressReporter : IProgressReporter
    {
        public static readonly NullProgressReporter Instance = new();
        
        private NullProgressReporter() { }
        
        public void ReportProgress(string step, string message, int? current = null, int? total = null, object? details = null)
        {
            // Do nothing
        }
    }
}
