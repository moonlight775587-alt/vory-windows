using System.Windows;
using System.Windows.Controls;
using VoryWindows.ViewModels;

namespace VoryWindows.Views.Controls
{
    public class ChatTemplateSelector : DataTemplateSelector
    {
        public DataTemplate UserTemplate { get; set; }
        public DataTemplate AssistantTemplate { get; set; }
        public DataTemplate ToolTemplate { get; set; }
        public DataTemplate TodoTemplate { get; set; }
        public DataTemplate ApprovalTemplate { get; set; }
        public DataTemplate PromptTemplate { get; set; }
        public DataTemplate NoticeTemplate { get; set; }
        public DataTemplate StatsTemplate { get; set; }
        public DataTemplate SystemTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var chat = item as ChatItem;
            if (chat == null) return SystemTemplate;
            switch (chat.Kind)
            {
                case ChatItemKind.User: return UserTemplate;
                case ChatItemKind.Assistant: return AssistantTemplate;
                case ChatItemKind.Tool: return ToolTemplate;
                case ChatItemKind.Todo: return TodoTemplate;
                case ChatItemKind.Approval: return ApprovalTemplate;
                case ChatItemKind.Prompt: return PromptTemplate;
                case ChatItemKind.Notice: return NoticeTemplate;
                case ChatItemKind.Stats: return StatsTemplate;
                default: return SystemTemplate;
            }
        }
    }
}
