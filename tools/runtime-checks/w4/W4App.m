classdef W4App < matlab.apps.App

    % Properties that correspond to app components
    properties (Access = public)
        UIFigure   matlab.ui.Figure
        EditField  matlab.ui.control.EditField
        B          matlab.ui.control.Button
        L          matlab.ui.control.Label
        A          matlab.ui.control.Button
        D          matlab.ui.control.Button
    end

end

%[appendix]
%---
%[app:layout]
%{
<?xml version='1.0' encoding='UTF-8'?>
<Components>
    <UIFigure name='UIFigure'>
        <Name>'MATLAB App'</Name>
        <Position>[100 100 640 480]</Position>
        <Children>
            <Button name='D'>
                <Position>[200 300 100 22]</Position>
                <Text>'D'</Text>
            </Button>
            <Button name='A'>
                <Position>[20 400 100 22]</Position>
                <Text>'A'</Text>
            </Button>
            <Label name='L'>
                <Position>[20 300 100 22]</Position>
                <Text>'L'</Text>
            </Label>
            <Button name='B'>
                <Position>[200 400 100 22]</Position>
                <Text>'B'</Text>
            </Button>
            <EditField name='EditField'>
                <Position>[20 100 100 22]</Position>
            </EditField>
        </Children>
    </UIFigure>
</Components>
%}

%---
%[app:appDetails]
%{
<?xml version='1.0' encoding='UTF-8'?>
<AppDetails>
    <Name>W4App</Name>
    <Version>1.0</Version>
</AppDetails>
%}

%---
%[app:internalData]
%{
<?xml version='1.0' encoding='UTF-8'?>
<InternalData>
    <AppId>a68791da-6e6f-4c1d-bc44-5a09a803d6f2</AppId>
    <AppType>Standard</AppType>
    <MATLABRelease>R2025b</MATLABRelease>
    <MinimumSupportedMATLABRelease>R2025a</MinimumSupportedMATLABRelease>
</InternalData>
%}

%---
%[app:thumbnail]
%{
<!-- Thumbnail is used by file previewers. To change how the thumbnail is captured or stored, use the App Details dialog box in App Designer. -->
<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>
<Thumbnail autoCapture='true'></Thumbnail>
%}