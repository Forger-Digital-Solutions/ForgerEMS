namespace VentoyToolkitSetup.Wpf.Models;

// Phase 1 tri-state representation for a USB Builder category card. The
// Recommended state is distinct from Full so the UI can show that the user
// accepted a curated baseline (Tier == Recommended ∪ Required) versus
// selecting every item including large ISOs/tools.
public enum UsbBuilderProfileCategorySelectionState
{
    None,
    Partial,
    Recommended,
    Full
}
