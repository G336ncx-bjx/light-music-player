using System;

namespace Skylark
{
    /// <summary>
    /// 应用内弹窗卡片上的一个按钮。
    /// 样式用主界面现成的按钮样式（PrimaryButton / OutlineButton / DangerButton / GhostButton）。
    /// </summary>
    public sealed class ModalAction
    {
        public string Label;
        public string StyleKey;
        public Action OnClick;
        /// <summary>点完是否自动关掉弹窗（默认关）。</summary>
        public bool AutoClose = true;

        public ModalAction(string label, string styleKey, Action onClick)
        {
            Label = label;
            StyleKey = styleKey == null ? "OutlineButton" : styleKey;
            OnClick = onClick;
        }
    }
}
