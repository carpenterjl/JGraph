# ADR 0293 — App Details and Help Text

## Status

Accepted, 2026-10-31. Stage W8a of the App Designer window plan: App Details, the app's own rows in the inspector, a custom thumbnail and the Help Text dialog. W8 is split into W8a, W8b (Run with inputs), W8c (Convert, Save Copy As, Export) and W8d (the Code View's leftovers).

## Context

JGraph kept an `.mlapp`'s details untouched and a plain-text app's as read, with nothing to edit either. App Input Arguments opened a small dialog of its own, the app's row in the browser showed only its name in the inspector, and the comment lines under `classdef` were kept as text with no way to write them.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `appdesigner/view/dialogs/appdetails/AppDetailsDialogController`, `AppDetailsDialogContent`, `PlainTextAppDetailsDialogContent`, `AppDetailsCodeOptionsContent`, `appdesigner/util/AppDetailsUtil`, `appdesigner/command/SetAppDetailsCommand`, `HelpCommentsCommand`, `appdesigner/view/dialogs/helpcomments/HelpCommentsDialogController`, `appdesigner/codegen/util/CodeFormatter`, `appdesigner/codegen/builders/ClassBuilder`, `appdesigner/controller/inspectablerootnode/InspectableAppNodeController`, `InspectableAppKabob`, and `appdesigner/model/inspectableapp/_InspectableRootNodeTranslator`, `InspectableAppClientDrivenModel`; MATLAB's side too: `appdesigner.internal.serialization.FileWriter`, `loader.HelpCommentsParser`, `application.AppThumbnailUtils`, `application.ImageUtils`, `model.MetadataModel`.

## Decision

- **App Details' OK is one step** (`SetAppDetailsCommand`): whichever of the details, the input arguments and the single instance differ from what the app holds. A version that will not do leaves the one held, and input arguments that will not do are left.
- **Two checks of a version.** While typed, `^([0-9]?|[0-9]+\.)$` lets part of one through; on leaving the field, `^[0-9]+\.[0-9]+$`, whose message goes under it, and OK waits. A package asks less (`FileWriter.validateVersion`, `^[0-9]+\.[0-9]+`): `3.0 x` and `1.2.3` are kept, `abc` becomes `1.0`. A version held already, even `1.2.3`, is not refused until it changes.
- **The two dialogs**: an accordion of Sharing Details and Code Options, one open at a time. An `.mlapp`'s has Author up to 42 and no thumbnail source, so a picture chosen is its only way to `manual`. A plain-text app's has Author up to 60 and Auto, Custom and None, each change clearing the picture shown. App Input Arguments and a group's kebab open it at their section.
- **A thumbnail in a package** (measured, `ui-probes/w8/w8_details`): the file's bytes as they are in `metadata/appScreenshot.<format>`, under two relationships (`appScreenshot` and the package's `thumbnail`) and a content type for the extension. A later picture of another format replaces the part and its relationships. **In a plain-text app**, a JPEG fitted within 220 by 165 in the appendix.
- **What is not edited is not written.** A save with no details changed keeps the package's metadata parts as their bytes (`_detailsChanged`), as JGraph's save keeps every other part. Reading the details changes nothing.
- **The details go across the formats**, thumbnail included, in either direction, and the mode with them.
- **The app's rows** (`InspectableAppNodeController`): Sharing Details (Name, Version, Author, Summary, Description) then Code Options (Single Running Instance, Input Arguments), each group's kebab opening App Details. A version or input argument that will not do is refused in App Designer's alert (`_handleVersionSet`, `_handleInputArgumentsSet`).
- **Help text** (`HelpCommentsDialogController`, `ClassBuilder._resetStartLines`, `HelpCommentsParser`): `%CLASSNAME summary` then `%   description` under the class line, wrapped to 75 less the indent by `wrapComment`, one step. A description is wrapped when OK holds it, so the dialog next shows what the file says. Comments not headed by the class's name in capitals are no help text, and stay as they are.
- **Help text follows the class.** A Save As writes `%NEWNAME`, as `ClassBuilder` writes the lines from the summary with the name it holds. So the class takes its new name before the text is written, in all three of Save As's branches.
- **Words are R2025b's**: the action `App\nHelp Text`, `App Help Text`, `Summary of the app`, `Detailed explanation`, every message and tooltip of App Details.

## Consequences

- `DesignDocument.AppDetails` holds both formats' details in `DesignPlainTextDetails`, read from the package the first time an `.mlapp`'s are asked for; `Values` and `DesignAppDetailsValues` are what an Undo puts back.
- `SetInputParameters` is split, so that App Details' step can hold its edits.
- A blank line in a description is gone once the help text is read back, here as in R2025b's parser, which was measured to drop it.
- `AppDesignerDialogs.CodeOptions` is gone. Its automation ids stay on the new dialog's two fields.
- The `.mlapp` window check opens `tools/runtime-checks/w8/W8aBin.mlapp`, written by JGraph's own writer (`JGRAPH_W8A_FIXTURE`).

## Measured

- **R2025b's own code**: `ui-probes/w8/oracle/details.js` loads `AppDetailsUtil`, `CodeEditUtil`, `CodeFormatter` and `ClassBuilder` whole, and cuts both dialogs' `_handleOkButton` out of the bundle: 143 versions, 13 images, 300 wraps, 300 sets of help lines, 200 descriptions and 400 OKs in `DesignAppDetailsOracle.json`, replayed by `DesignAppDetailsOracleTests` (6).
- **R2025b's MATLAB side, headless**: `ui-probes/w8/w8_details.m` writes details and a picture with `FileWriter` into a copy of an app, listing the parts, the versions kept, the sizes fitted, and the appendix's section. `w8_read_back.m` then reads what JGraph's window check saved: `MLAPPMetadataReader`, `getThumbnailDataURI`, `loadApp` (outcome success, help text, input arguments, single instance, mode) and `MAppMetadataReader` give back every value.
- **Tests**: `AppDesignerDetailsTests` (40 with its theories). With the designer, App Designer, toolstrip and settings suites: 1058 pass.
- **The window check** (`run-w8adesigner.ps1`, JGraph.Application in Release over an `.mlapp` and a plain-text app): all 106 checks pass, each edit read back from the file. I looked at pictures of both dialogs, the Help Text dialog and the app's rows.

## Not measured

- **R2025b's App Details and Help Text windows themselves**: their code was run under node over stand-ins, and their look read from its style sheet, not compared on screen.
- **A thumbnail captured by Run**: that comes with W8b.
- **The common file dialog by UI Automation**: its name box is not in the tree the check reaches, so the check sets it by a message to the dialog's own handle.
- **Hovering over the picture**: JGraph's button stands there all the time.

## Divergences

- **A plain-text thumbnail really fits within 220 by 165.** R2025b's `createAppThumbnailDataURI` looks at the longer side alone, so a square picture stays any size and 221 by 166 keeps its height of 166 (measured), where its comment says it fits. Not kept.
- **A plain-text app's None and Custom stay None and Custom when opened again**, told from `autoCapture` and whether the file holds a picture. R2025b's `MAppMetadataReader` gives `manual` for `autoCapture='false'` with a picture; its answer with none was not measured.
- **The app's rows** have no property search or alphabetical view, and the Summary and Description rows are text areas showing their lines, where R2025b's `split("\n").join(",")` shows commas in a row and takes an array back. Each row's edit is a step of its own that Undo takes back.
- **The picture's button** stands in view, where R2025b's fades in on hover. The dialogs have JGraph's look; the help dialog's comment colour is one green, not the theme's.
- **An image that cannot be decoded is refused when chosen**, in either format, where R2025b's `.mlapp` dialog takes any file with the extension, reads its bytes and simply draws no picture.
