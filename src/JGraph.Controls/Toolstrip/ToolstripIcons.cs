using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using JGraph.Controls.Themes;

namespace JGraph.Controls.Toolstrip;

/// <summary>
/// JGraph's own toolstrip icons (App Designer window plan, decision WO1: nothing MathWorks drew),
/// named as R2025b's icon set names them so a recorded layout finds its pictures. Each is a few
/// strokes and fills on a 16-unit square, drawn at 16 or 24 pixels in the text colour, with a fixed
/// colour where the meaning needs one (run green, stop red).
/// </summary>
public static class ToolstripIcons
{
    // Each icon is "role|path" parts separated by ';'. Roles: i ink stroke, I ink fill, p paper fill
    // (the surface colour), g/G green, r/R red, b/B blue, a/A amber (lower case strokes, upper case fills).
    private static readonly Dictionary<string, string> Specs = new(StringComparer.Ordinal)
    {
        ["new"] = "p|M3.5,1.5H9.5L12.5,4.5V14.5H3.5Z;i|M3.5,1.5H9.5L12.5,4.5V14.5H3.5Z;i|M9.5,1.5V4.5H12.5;G|M11,8h2v2h2v2h-2v2h-2v-2h-2v-2h2z",
        ["new_16"] = "p|M3.5,1.5H9.5L12.5,4.5V14.5H3.5Z;i|M3.5,1.5H9.5L12.5,4.5V14.5H3.5Z;i|M9.5,1.5V4.5H12.5;G|M11,8h2v2h2v2h-2v2h-2v-2h-2v-2h2z",
        ["openFolder"] = "A|M1.5,4.5H6L7.5,6H14.5V13.5H1.5Z;i|M1.5,4.5H6L7.5,6H14.5V13.5H1.5Z;i|M1.5,8.5H14.5",
        ["saved"] = "B|M2.5,2.5H11.5L13.5,4.5V13.5H2.5Z;p|M5,2.5H11V6H5Z;p|M4.5,9H11.5V13.5H4.5Z;i|M2.5,2.5H11.5L13.5,4.5V13.5H2.5Z;i|M6,9.5V11.5",
        ["saveAs"] = "B|M1.5,2.5H9.5L11.5,4.5V12.5H1.5Z;p|M4,2.5H9V5.5H4Z;i|M1.5,2.5H9.5L11.5,4.5V12.5H1.5Z;A|M9,15L9.5,12.5L14,8L15.5,9.5L11,14Z;i|M9,15L9.5,12.5L14,8L15.5,9.5L11,14Z",
        ["saveCopyAs"] = "B|M1.5,2.5H9.5L11.5,4.5V12.5H1.5Z;p|M4,2.5H9V5.5H4Z;i|M1.5,2.5H9.5L11.5,4.5V12.5H1.5Z;G|M11,9h2v2h2v2h-2v2h-2v-2h-2v-2h2z",
        ["export_data"] = "p|M2.5,1.5H8.5L11.5,4.5V14.5H2.5Z;i|M2.5,1.5H8.5L11.5,4.5V14.5H2.5Z;i|M8.5,1.5V4.5H11.5;g|M7,10H15;g|M12.5,7.5L15,10L12.5,12.5",
        ["compare"] = "p|M1.5,2.5H7.5V13.5H1.5Z;p|M8.5,2.5H14.5V13.5H8.5Z;i|M1.5,2.5H7.5V13.5H1.5Z;i|M8.5,2.5H14.5V13.5H8.5Z;r|M3,6H6;g|M10,6H13;i|M3,9H6M10,9H13",
        ["toolsColor"] = "B|M10.5,1.5A4,4 0 0 0 7,6.5L1.5,12L4,14.5L9.5,9A4,4 0 0 0 14.5,5.5L12,8L9.5,7.5L8.5,5Z;i|M10.5,1.5A4,4 0 0 0 7,6.5L1.5,12L4,14.5L9.5,9A4,4 0 0 0 14.5,5.5L12,8L9.5,7.5L8.5,5Z",
        ["packageApp"] = "p|M3.5,1.5H9.5L12.5,4.5V14.5H3.5Z;i|M3.5,1.5H9.5L12.5,4.5V14.5H3.5Z;i|M9.5,1.5V4.5H12.5;B|M5.5,7h5v1.5h-5z;B|M5.5,10h5v1.5h-5z",
        ["details_app"] ="p|M2.5,1.5H13.5V14.5H2.5Z;i|M2.5,1.5H13.5V14.5H2.5Z;B|M7,3.5h2v2h-2z;B|M7,7h2v5.5h-2z",
        ["applicationCompilerApp"] = "A|M1.5,6.5H11.5V14.5H1.5Z;i|M1.5,6.5H11.5V14.5H1.5Z;i|M1.5,6.5L4,3.5H9L11.5,6.5;g|M9,4L14.5,1.5;g|M11,1.5H14.5V5",
        ["playControl"] = "G|M4,2.5L13.5,8L4,13.5Z;g|M4,2.5L13.5,8L4,13.5Z",
        ["continue"] = "G|M4,2.5L13.5,8L4,13.5Z;g|M4,2.5L13.5,8L4,13.5Z",
        ["pause"] = "B|M3.5,2.5h3v11h-3z;B|M9.5,2.5h3v11h-3z",
        ["stop"] = "R|M3,3h10v10h-10z",
        ["stepForward"] = "i|M2.5,9A5.5,5.5 0 0 1 13.5,9;b|M10.5,8.5L13.5,9L14.5,6;I|M7,12.5h2v2h-2z",
        ["stepForwardIn"] = "b|M8,1.5V10;b|M5,7L8,10L11,7;I|M7,12.5h2v2h-2z",
        ["stepForwardOut"] = "b|M8,10V1.5;b|M5,4.5L8,1.5L11,4.5;I|M7,12.5h2v2h-2z",
        ["printer"] = "p|M4.5,1.5H11.5V5.5H4.5Z;i|M4.5,1.5H11.5V5.5H4.5Z;I|M1.5,5.5H14.5V11.5H1.5Z;p|M4.5,9.5H11.5V14.5H4.5Z;i|M4.5,9.5H11.5V14.5H4.5Z",
        ["convertApp"] = "p|M1.5,2.5H6.5V13.5H1.5Z;p|M9.5,2.5H14.5V13.5H9.5Z;i|M1.5,2.5H6.5V13.5H1.5Z;i|M9.5,2.5H14.5V13.5H9.5Z;i|M12,2.5V13.5;b|M5,8H11;b|M9,6L11,8L9,10",
        ["autoReflowTwoPanel"] = "p|M1.5,2.5H14.5V13.5H1.5Z;i|M1.5,2.5H14.5V13.5H1.5Z;i|M6.5,2.5V13.5",
        ["autoReflowThreePanel"] = "p|M1.5,2.5H14.5V13.5H1.5Z;i|M1.5,2.5H14.5V13.5H1.5Z;i|M5.5,2.5V13.5M10.5,2.5V13.5",
        ["alignLeft"] = "i|M1.5,1V15;B|M3,3h9v3h-9z;B|M3,9h6v3h-6z",
        ["alignHorizontalCenter"] = "i|M8,1V15;B|M3,3h10v3h-10z;B|M5,9h6v3h-6z",
        ["alignRight"] = "i|M14.5,1V15;B|M4,3h9v3h-9z;B|M7,9h6v3h-6z",
        ["alignTop"] = "i|M1,1.5H15;B|M3,3h3v9h-3z;B|M9,3h3v6h-3z",
        ["alignVerticalCenter"] = "i|M1,8H15;B|M3,3h3v10h-3z;B|M9,5h3v6h-3z",
        ["alignBottom"] = "i|M1,14.5H15;B|M3,4h3v9h-3z;B|M9,7h3v6h-3z",
        ["makeSameSize"] = "B|M1.5,1.5h6v6h-6z;i|M8.5,8.5h6v6h-6z;i|M2,14L7,9;i|M7,11.5V9H4.5",
        ["makeSameWidth"] = "B|M1.5,2h13v4h-13z;i|M1.5,10.5h13v4h-13z;i|M1.5,7.5V9.5M14.5,7.5V9.5",
        ["makeSameHeight"] = "B|M2,1.5h4v13h-4z;i|M10.5,1.5h4v13h-4z;i|M7.5,1.5H9.5M7.5,14.5H9.5",
        ["group"] = "i|M1.5,1.5H14.5V14.5H1.5Z;B|M3.5,3.5h4v4h-4z;G|M8.5,8.5h4v4h-4z",
        ["ungroup"] = "B|M1.5,1.5h5v5h-5z;G|M9.5,9.5h5v5h-5z;i|M9.5,1.5H14.5V6.5M1.5,9.5V14.5H6.5",
        ["reorder"] = "p|M5.5,5.5h9v9h-9z;i|M5.5,5.5h9v9h-9z;B|M1.5,1.5h8v8h-8z",
        ["bringToFront"] = "i|M1.5,1.5h6v6h-6z;i|M8.5,8.5h6v6h-6z;B|M4.5,4.5h7v7h-7z",
        ["bringForward"] = "i|M1.5,1.5h8v8h-8z;B|M6.5,6.5h8v8h-8z",
        ["sendBackward"] = "B|M1.5,1.5h8v8h-8z;p|M6.5,6.5h8v8h-8z;i|M6.5,6.5h8v8h-8z",
        ["sendToBack"] = "B|M4.5,4.5h7v7h-7z;p|M1.5,1.5h6v6h-6z;p|M8.5,8.5h6v6h-6z;i|M1.5,1.5h6v6h-6z;i|M8.5,8.5h6v6h-6z",
        ["changeSelectionOrder"] = "B|M1.5,6h3v4h-3z;B|M6.5,6h3v4h-3z;B|M11.5,6h3v4h-3z;i|M3,4C5,1.5 11,1.5 13,4;i|M11.5,3.5L13,4L13.5,2.5",
        ["evenHorizontalGaps"] = "B|M1.5,3h3v10h-3z;B|M6.5,3h3v10h-3z;B|M11.5,3h3v10h-3z;i|M4.5,15H6.5M9.5,15H11.5",
        ["evenVerticalGaps"] = "B|M3,1.5h10v3h-10z;B|M3,6.5h10v3h-10z;B|M3,11.5h10v3h-10z;i|M1,4.5V6.5M1,9.5V11.5",
        ["grid"] = "i|M1.5,1.5H14.5V14.5H1.5Z;i|M5.83,1.5V14.5M10.17,1.5V14.5M1.5,5.83H14.5M1.5,10.17H14.5",
        ["snapToGrid"] = "i|M1.5,1.5H14.5V14.5H1.5Z;i|M5.83,1.5V14.5M10.17,1.5V14.5M1.5,5.83H14.5M1.5,10.17H14.5;R|M4.3,4.3h3v3h-3z",
        ["hintsAlignment"] = "B|M1.5,4h5v5h-5z;B|M9.5,4h5v3h-5z;r|M0.5,4H15.5",
        ["hintsResizing"] = "i|M2.5,2.5h11v11h-11z;b|M8,4.5V11.5M4.5,8H11.5;b|M6.5,6L8,4.5L9.5,6M6.5,10L8,11.5L9.5,10",
        ["fitToView"] = "i|M1.5,5V1.5H5M11,1.5H14.5V5M14.5,11V14.5H11M5,14.5H1.5V11;B|M5,5h6v6h-6z",
        ["add_callback"] = "i|M6,1.5C4,1.5 4.5,4.5 4.5,6C4.5,7 3.5,8 2.5,8C3.5,8 4.5,9 4.5,10C4.5,11.5 4,14.5 6,14.5;G|M10,6h2v2h2v2h-2v2h-2v-2h-2v-2h2z",
        ["add_callbackFunction"] = "i|M9.5,2C7.5,1.5 6.5,2.5 6.5,4.5V12C6.5,14 5.5,14.5 4,14;i|M4,7H9;G|M11,9h2v2h2v2h-2v2h-2v-2h-2v-2h2z",
        ["add_callbackProperty"] = "B|M2.5,3.5H9L11.5,6L9,8.5H2.5Z;i|M2.5,3.5H9L11.5,6L9,8.5H2.5Z;G|M11,9h2v2h2v2h-2v2h-2v-2h-2v-2h2z",
        ["add_event"] = "A|M8,1L3,8.5H7L6,15L11.5,6.5H7.5Z;i|M8,1L3,8.5H7L6,15L11.5,6.5H7.5Z",
        ["edit"] = "A|M2,14L2.5,11L11,2.5L13.5,5L5,13.5Z;i|M2,14L2.5,11L11,2.5L13.5,5L5,13.5Z;i|M9.5,4L12,6.5",
        ["edit_appHelpText"] = "p|M2.5,1.5H13.5V14.5H2.5Z;i|M2.5,1.5H13.5V14.5H2.5Z;b|M6,6C6,4.5 7,3.8 8,3.8C9,3.8 10,4.5 10,5.7C10,7.2 8,7.2 8,9;B|M7.25,11h1.5v1.5h-1.5z",
        ["edit_appInputArguments"] = "p|M5.5,1.5H14.5V14.5H5.5Z;i|M5.5,1.5H14.5V14.5H5.5Z;g|M1,8H9.5;g|M7,5.5L9.5,8L7,10.5",
        ["link"] = "i|M7,9.5L9.5,7;i|M6,6.5L4,8.5A2.1,2.1 0 0 0 7,11.5L9,9.5;i|M10,9.5L12,7.5A2.1,2.1 0 0 0 9,4.5L7,6.5",
        ["simulink"] = "B|M1.5,5.5h4v5h-4z;B|M10.5,5.5h4v5h-4z;i|M5.5,8H10.5;i|M8.5,6.5L10.5,8L8.5,9.5",
        ["validateDesign"] = "g|M2.5,8.5L6,12L13.5,4",
        ["showTips"] = "A|M8,1.5A4.5,4.5 0 0 0 5.5,9.8V11.5H10.5V9.8A4.5,4.5 0 0 0 8,1.5Z;i|M8,1.5A4.5,4.5 0 0 0 5.5,9.8V11.5H10.5V9.8A4.5,4.5 0 0 0 8,1.5Z;i|M6,13.5H10",
        ["check_formatText"] = "i|M1.5,3.5H10.5M1.5,6.5H8.5M1.5,9.5H6.5;g|M8,11L10.5,13.5L14.5,8",
        ["cut"] = "i|M4,1.5L11,11M12,1.5L5,11;i|M4,13A2,2 0 1 0 4,9A2,2 0 1 0 4,13Z;i|M12,13A2,2 0 1 0 12,9A2,2 0 1 0 12,13Z",
        ["copy"] = "p|M1.5,1.5H9.5V11.5H1.5Z;i|M1.5,1.5H9.5V11.5H1.5Z;p|M5.5,4.5H13.5V14.5H5.5Z;i|M5.5,4.5H13.5V14.5H5.5Z",
        ["paste"] = "A|M2.5,2.5H12.5V14.5H2.5Z;i|M2.5,2.5H12.5V14.5H2.5Z;I|M5.5,1h4v3h-4z;p|M6.5,6.5H14.5V15H6.5Z;i|M6.5,6.5H14.5V15H6.5Z",
        ["undo"] = "b|M4,6.5H10A3.5,3.5 0 0 1 10,13.5H6;b|M6.5,4L4,6.5L6.5,9",
        ["redo"] = "b|M12,6.5H6A3.5,3.5 0 0 0 6,13.5H10;b|M9.5,4L12,6.5L9.5,9",
        ["help"] = "B|M8,1A7,7 0 1 0 8,15A7,7 0 1 0 8,1Z;w|M5.8,6C5.8,4.5 6.8,3.8 8,3.8C9.2,3.8 10.2,4.6 10.2,5.8C10.2,7.5 8,7.4 8,9.5;W|M7.2,11h1.6v1.6h-1.6z",
        ["find"] = "i|M6.5,1.5A5,5 0 1 0 6.5,11.5A5,5 0 1 0 6.5,1.5Z;i|M10,10L14.5,14.5",
        ["goToLine"] = "i|M1.5,3.5H9.5M1.5,6.5H7.5M1.5,9.5H9.5M1.5,12.5H7.5;b|M10,8H15;b|M12.5,5.5L15,8L12.5,10.5",
        ["bookmark"] = "B|M4,1.5H12V14.5L8,11L4,14.5Z;i|M4,1.5H12V14.5L8,11L4,14.5Z",
        ["zoomIn"] = "i|M6.5,1.5A5,5 0 1 0 6.5,11.5A5,5 0 1 0 6.5,1.5Z;i|M10,10L14.5,14.5;i|M4,6.5H9M6.5,4V9",
        ["zoomOut"] = "i|M6.5,1.5A5,5 0 1 0 6.5,11.5A5,5 0 1 0 6.5,1.5Z;i|M10,10L14.5,14.5;i|M4,6.5H9",
        ["arrowGoTo"] = "b|M2.5,13.5V8.5A3,3 0 0 1 5.5,5.5H13.5;b|M11,3L13.5,5.5L11,8",
        ["arrowNavigationNorth"] = "b|M8,14.5V2;b|M4,6L8,2L12,6",
        ["arrowNavigationSouth"] = "b|M8,1.5V14;b|M4,10L8,14L12,10",
        ["breakpoint_code"] = "R|M8,3A5,5 0 1 0 8,13A5,5 0 1 0 8,3Z",
        ["clear_breakpoints"] = "R|M6.5,2A4.5,4.5 0 1 0 6.5,11A4.5,4.5 0 1 0 6.5,2Z;i|M10.5,10.5L14.5,14.5M14.5,10.5L10.5,14.5",
        ["disable_breakpoint"] = "r|M8,3A5,5 0 1 0 8,13A5,5 0 1 0 8,3Z;i|M3,13L13,3",
        ["set_conditionalBreakpoint"] = "A|M8,2A6,6 0 1 0 8,14A6,6 0 1 0 8,2Z;i|M6,6.5C6,5 7,4.5 8,4.5C9,4.5 10,5 10,6.2C10,7.5 8,7.5 8,9.2;I|M7.3,10.5h1.4v1.4h-1.4z",
        ["comment"] = "g|M4,13.5L12,2.5;G|M2.5,2.5h3v3h-3z;G|M10.5,10.5h3v3h-3z",
        ["clear_comment"] = "i|M4,13.5L12,2.5;I|M2.5,2.5h3v3h-3z;I|M10.5,10.5h3v3h-3z;r|M1.5,8H14.5",
        ["wrap_comment"] = "g|M1.5,3.5H12.5M1.5,13.5H7;b|M12.5,3.5V9.5H6;b|M8,7.5L6,9.5L8,11.5",
        ["codeExpand"] = "p|M2.5,2.5h11v11h-11z;i|M2.5,2.5h11v11h-11z;i|M5,8H11M8,5V11",
        ["codeCollapse"] = "p|M2.5,2.5h11v11h-11z;i|M2.5,2.5h11v11h-11z;i|M5,8H11",
        ["codeHighlighted"] = "A|M1,6h14v4h-14z;i|M2.5,3.5H12.5M2.5,8H10.5M2.5,12.5H12.5",
        ["dataTip"] = "p|M1.5,2.5H14.5V10.5H7L4.5,13.5V10.5H1.5Z;i|M1.5,2.5H14.5V10.5H7L4.5,13.5V10.5H1.5Z;i|M4,5.5H12M4,8H9",
        ["indentCodeAuto"] = "i|M1.5,3.5H10.5M4.5,6.5H12.5M4.5,9.5H10.5M1.5,12.5H8.5;A|M13,9.5L13.8,11.2L15.5,12L13.8,12.8L13,14.5L12.2,12.8L10.5,12L12.2,11.2Z",
        ["indentCodeRight"] = "i|M7,3.5H14.5M7,6.5H14.5M7,9.5H14.5M7,12.5H14.5;b|M1.5,5.5L4.5,8L1.5,10.5",
        ["indentCodeLeft"] = "i|M7,3.5H14.5M7,6.5H14.5M7,9.5H14.5M7,12.5H14.5;b|M4.5,5.5L1.5,8L4.5,10.5",
        ["lineNumbers"] = "B|M1.5,2.5h2.5v2h-2.5zM1.5,7h2.5v2h-2.5zM1.5,11.5h2.5v2h-2.5z;i|M6,3.5H14.5M6,8H14.5M6,12.5H14.5",
        ["singleTile"] = "p|M2.5,2.5h11v11h-11z;i|M2.5,2.5h11v11h-11z",
        ["splitDocHorizontally"] = "p|M2.5,2.5h11v11h-11z;i|M2.5,2.5h11v11h-11z;i|M8,2.5V13.5",
        ["splitDocVertically"] = "p|M2.5,2.5h11v11h-11z;i|M2.5,2.5h11v11h-11z;i|M2.5,8H13.5",
        ["zoomReset"] = "i|M6.5,1.5A5,5 0 1 0 6.5,11.5A5,5 0 1 0 6.5,1.5Z;i|M10,10L14.5,14.5;i|M4,5.5H9M4,7.5H9",
        ["section_24"] = "B|M1.5,1.5h5v5h-5z;B|M9.5,1.5h5v5h-5z;B|M1.5,9.5h5v5h-5z;i|M9.5,9.5h5v5h-5z",
        ["dropDown"] = "I|M4.5,6.5H11.5L8,10Z",
        ["collapse"] = "i|M4,10L8,6L12,10",
        ["expand"] = "i|M4,6L8,10L12,6",
    };

    private static readonly Dictionary<string, (char Role, Geometry Geometry)[]> Parsed = new(StringComparer.Ordinal);

    /// <summary>The names of every icon drawn.</summary>
    public static IReadOnlyCollection<string> Names => Specs.Keys;

    /// <summary>Whether an icon of that name is drawn.</summary>
    public static bool Has(string? name) => name is not null && Specs.ContainsKey(name);

    /// <summary>An element drawing the icon of that name at the size, or null when there is none.</summary>
    public static FrameworkElement? Create(string? name, double size)
    {
        if (!Has(name))
        {
            return null;
        }

        return new ToolstripIconElement(name!) { Width = size, Height = size, SnapsToDevicePixels = true };
    }

    internal static (char Role, Geometry Geometry)[] Parts(string name)
    {
        if (!Parsed.TryGetValue(name, out (char, Geometry)[]? parts))
        {
            parts = [.. Specs[name].Split(';').Select(p => (p[0], Geometry.Parse(p[2..])))];
            foreach ((char _, Geometry g) in parts)
            {
                g.Freeze();
            }

            Parsed[name] = parts;
        }

        return parts;
    }

    internal static Brush? Fixed(char role) => char.ToLowerInvariant(role) switch
    {
        'g' => Frozen(0x2E, 0x9A, 0x3C),
        'r' => Frozen(0xD0, 0x3A, 0x3A),
        'b' => Frozen(0x2D, 0x7F, 0xD3),
        'a' => Frozen(0xE6, 0xA5, 0x2E),
        'w' => Brushes.White,
        _ => null,
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Draws one of <see cref="ToolstripIcons"/> in the inherited text colour.</summary>
internal sealed class ToolstripIconElement : FrameworkElement
{
    // The surface colour, followed through a theme change.
    private static readonly DependencyProperty PaperProperty = DependencyProperty.Register(
        "Paper", typeof(Brush), typeof(ToolstripIconElement), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly string _name;

    static ToolstripIconElement()
    {
        TextElement.ForegroundProperty.OverrideMetadata(typeof(ToolstripIconElement),
            new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public ToolstripIconElement(string name)
    {
        _name = name;
        Focusable = false;
        IsHitTestVisible = false;
        SetResourceReference(PaperProperty, ThemeKeys.Surface);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 16.0;
        if (scale <= 0)
        {
            return;
        }

        Brush ink = TextElement.GetForeground(this) ?? Brushes.Black;
        var paper = (Brush)GetValue(PaperProperty);
        dc.PushTransform(new ScaleTransform(scale, scale));
        foreach ((char role, Geometry geometry) in ToolstripIcons.Parts(_name))
        {
            Brush brush = role switch
            {
                'i' or 'I' => ink,
                'p' => paper,
                _ => ToolstripIcons.Fixed(role) ?? ink,
            };
            if (char.IsUpper(role) || role == 'p')
            {
                dc.DrawGeometry(brush, null, geometry);
            }
            else
            {
                dc.DrawGeometry(null, new Pen(brush, 1.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
            }
        }

        dc.Pop();
    }
}
