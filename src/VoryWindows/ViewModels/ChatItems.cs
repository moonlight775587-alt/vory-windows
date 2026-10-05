using System;
using System.Collections.Generic;
using VoryWindows.Models;
using VoryWindows.Mvvm;

namespace VoryWindows.ViewModels
{
    public enum ChatItemKind
    {
        User,
        Assistant,
        Tool,
        Todo,
        Approval,
        Prompt,      // clarify / sudo / secret / vault
        Notice,      // bot-to-bot
        Stats,
        System,
        Error
    }

    public class TodoEntry
    {
        public string Text { get; set; } = "";
        public bool Done { get; set; }
    }

    public class ChatItem : ViewModelBase
    {
        public ChatItemKind Kind { get; set; }

        private string _text = "";
        public string Text
        {
            get { return _text; }
            set { Set(ref _text, value); }
        }

        public string Role { get; set; } = "";
        public DateTime Time { get; set; } = DateTime.Now;

        // Tool card fields
        public string ToolName { get; set; } = "";
        public string ToolCommand { get; set; } = "";
        private string _toolOutput = "";
        public string ToolOutput
        {
            get { return _toolOutput; }
            set { Set(ref _toolOutput, value); }
        }
        public bool ToolRunning
        {
            get { return _toolRunning; }
            set { Set(ref _toolRunning, value); }
        }
        private bool _toolRunning;
        public List<TodoEntry> Todos { get; set; }

        // Approval / prompt card fields
        public PendingApproval Approval { get; set; }
        public PendingPrompt Prompt { get; set; }
        private bool _answered;
        public bool Answered
        {
            get { return _answered; }
            set { Set(ref _answered, value); }
        }

        public string TimeLabel { get { return Time.ToString("HH:mm"); } }
    }
}
