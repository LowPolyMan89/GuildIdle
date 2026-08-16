namespace GuildIdle.UI.Core
{
    public abstract class UIPanel : UIView
    {
        public void Show()
        {
            ShowForLifecycle();
        }

        public void Hide()
        {
            HideForLifecycle();
        }
    }
}
