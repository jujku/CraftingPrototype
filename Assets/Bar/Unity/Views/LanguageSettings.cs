using UnityEngine;

namespace Bar.Unity
{
    /// <summary>One settings entry; language only. Uses the session's existing cancellation and input gate.</summary>
    public sealed class LanguageSettings
    {
        private readonly BarSession session;
        private bool resumePointer;
        public bool IsOpen { get; private set; }
        public LanguageSettings(BarSession session) { this.session = session; UiText.Load(); }
        public Rect EntryRect => new Rect(Screen.width - 158, 18, 140, 30);
        public bool CapturesPointer(Vector2 screenPosition)
        {
            if (IsOpen) return true;
            if (Cursor.lockState == CursorLockMode.Locked) return false;
            screenPosition.y = Screen.height - screenPosition.y;
            return EntryRect.Contains(screenPosition) || session.DebugVisible && session.DebugPanel.Bounds.Contains(screenPosition);
        }
        public void Toggle()
        {
            if (!IsOpen)
            {
                resumePointer = Cursor.lockState == CursorLockMode.Locked;
                session.Cancel(session.EventTime(Time.realtimeSinceStartupAsDouble));
                IsOpen = true;
            }
            else
            {
                IsOpen = false;
                if (resumePointer && !session.Orders.ModalOpen && session.Sound.Gate.Unlocked) session.ResumePointer();
            }
        }
        public void Draw(Font font)
        {
            int oldDepth = GUI.depth; GUI.depth = -200;
            var button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14 };
            if (GUI.Button(EntryRect, UiText.T("设置 [F10]", "Settings [F10]"), button)) Toggle();
            if (IsOpen)
            {
                var r = new Rect((Screen.width - 340) / 2, (Screen.height - 210) / 2, 340, 210);
                var color = GUI.color; GUI.color = new Color(.035f, .045f, .055f, .98f);
                GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = color;
                var label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18 };
                GUI.Label(new Rect(r.x + 22, r.y + 18, 280, 30), UiText.T("设置", "Settings"), label);
                label.fontSize = 14;
                GUI.Label(new Rect(r.x + 22, r.y + 56, 280, 24), UiText.T("界面语言 / Language", "Language / 界面语言"), label);
                if (GUI.Toggle(new Rect(r.x + 22, r.y + 92, 92, 36), UiText.Language == UiLanguage.Chinese, "中文", button) && UiText.Language != UiLanguage.Chinese) UiText.SetLanguage(UiLanguage.Chinese);
                if (GUI.Toggle(new Rect(r.x + 124, r.y + 92, 92, 36), UiText.Language == UiLanguage.English, "English", button) && UiText.Language != UiLanguage.English) UiText.SetLanguage(UiLanguage.English);
                if (GUI.Toggle(new Rect(r.x + 226, r.y + 92, 92, 36), UiText.Language == UiLanguage.Japanese, "日本語", button) && UiText.Language != UiLanguage.Japanese) UiText.SetLanguage(UiLanguage.Japanese);
                if (GUI.Button(new Rect(r.x + 22, r.y + 151, 296, 34), UiText.T("完成 [Esc / F10]", "Done [Esc / F10]"), button)) Toggle();
            }
            GUI.depth = oldDepth;
        }
    }
}
