namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool IsReadableMapCell(int cell)
        {
            bool valid = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell);
            int world = ClusterManager.Instance == null ? -1 : ClusterManager.Instance.activeWorldId;
            return MapTextReadPolicy.CanRead(valid, valid && Grid.IsVisible(cell),
                valid ? Grid.WorldIdx[cell] : -1, world);
        }

        private static bool IsSupportedTextMapView(HashedString mode)
        {
            return mode == OverlayModes.None.ID || mode == OverlayModes.Power.ID
                || mode == OverlayModes.LiquidConduits.ID || mode == OverlayModes.GasConduits.ID
                || mode == OverlayModes.Logic.ID || mode == OverlayModes.SolidConveyor.ID
                || mode == OverlayModes.Temperature.ID || mode == OverlayModes.Oxygen.ID
                || mode == OverlayModes.Light.ID || mode == OverlayModes.Decor.ID
                || mode == OverlayModes.Disease.ID || mode == OverlayModes.Radiation.ID
                || mode == OverlayModes.TileMode.ID || mode == OverlayModes.Crop.ID
                || mode == OverlayModes.Harvest.ID || mode == OverlayModes.Rooms.ID;
        }
    }
}
