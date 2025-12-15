using System.Windows;

namespace SheetMusicViewer
{
    /// <summary>
    /// Service interface for showing message boxes.
    /// Allows for testing by providing mock implementations.
    /// </summary>
    public interface IMessageBoxService
    {
        void Show(string message);
        MessageBoxResult Show(string message, string caption, MessageBoxButton button);
    }

    /// <summary>
    /// Production implementation that shows actual MessageBox dialogs
    /// </summary>
    public class MessageBoxService : IMessageBoxService
    {
        public void Show(string message)
        {
            MessageBox.Show(message);
        }

        public MessageBoxResult Show(string message, string caption, MessageBoxButton button)
        {
            return MessageBox.Show(message, caption, button);
        }
    }

    /// <summary>
    /// Test implementation that suppresses MessageBox dialogs
    /// </summary>
    public class TestMessageBoxService : IMessageBoxService
    {
        public string LastMessage { get; private set; }
        public string LastCaption { get; private set; }
        public MessageBoxButton LastButton { get; private set; }
        public int ShowCallCount { get; private set; }

        public void Show(string message)
        {
            LastMessage = message;
            ShowCallCount++;
        }

        public MessageBoxResult Show(string message, string caption, MessageBoxButton button)
        {
            LastMessage = message;
            LastCaption = caption;
            LastButton = button;
            ShowCallCount++;
            return MessageBoxResult.OK; // Default response for tests
        }
    }
}
