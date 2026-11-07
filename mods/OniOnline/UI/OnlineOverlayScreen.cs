using UnityEngine;

namespace OniOnline.UI
{
    // CanvasRoot hosts a full-screen shade sibling of the KScreen panel.
    // KScreen.Deactivate only disables the panel, so without this binder the
    // shade + GraphicRaycaster stay alive at sortingOrder 30000+ and steal
    // clicks from native ConfirmDialog buttons.
    public abstract class OnlineOverlayScreen : KScreen
    {
        protected GameObject OverlayRoot;

        protected void BindOverlayRoot(GameObject root)
        {
            OverlayRoot = root;
        }

        protected override void OnActivate()
        {
            if (OverlayRoot != null) OverlayRoot.SetActive(true);
            base.OnActivate();
        }

        protected override void OnDeactivate()
        {
            base.OnDeactivate();
            if (OverlayRoot != null) OverlayRoot.SetActive(false);
        }
    }
}
