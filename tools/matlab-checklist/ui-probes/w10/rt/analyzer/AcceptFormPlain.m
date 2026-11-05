classdef AcceptFormPlain < matlab.apps.App

    % Properties that correspond to app components
    properties (Access = public)
        AcceptFormUIFigure  matlab.ui.Figure
        ResultLabel         matlab.ui.control.Label
        ComputeButton       matlab.ui.control.Button
        Tree                matlab.ui.container.CheckBoxTree
        Node                matlab.ui.container.TreeNode
        Node2               matlab.ui.container.TreeNode
        Node3               matlab.ui.container.TreeNode
        Node4               matlab.ui.container.TreeNode
        DatePicker          matlab.ui.control.DatePicker
        DatePickerLabel     matlab.ui.control.Label
        DoubleCheckBox      matlab.ui.control.CheckBox
        DropDown            matlab.ui.control.DropDown
        DropDownLabel       matlab.ui.control.Label
        Spinner             matlab.ui.control.Spinner
        SpinnerLabel        matlab.ui.control.Label
        EditField           matlab.ui.control.NumericEditField
        EditFieldLabel      matlab.ui.control.Label
    end

    % Callbacks that handle component events
    methods

        % Code that executes after component creation
        function startupFcn(app, n)
            app.EditField.Value = n;
        end

        % Button pushed function: ComputeButton
        function ComputeButtonPushed(app, event)
            a = app.EditField.Value;
            b = app.Spinner.Value;
            if strcmp(app.DropDown.Value, 'Add')
                r = a + b;
            else
                r = a * b;
            end
            if app.DoubleCheckBox.Value
                r = 2 * r;
            end
            app.ResultLabel.Text = sprintf('%g', r);
        end
    end

end

%[appendix]
%---
%[app:layout]
%{
<?xml version='1.0' encoding='UTF-8'?>
<Components>
    <UIFigure name='AcceptFormUIFigure'>
        <Name>'Accept Form'</Name>
        <Position>[100 100 640 480]</Position>
        <Children>
            <Label name='EditFieldLabel'>
                <HorizontalAlignment>'right'</HorizontalAlignment>
                <Position>[121 429 56 22]</Position>
                <Text>'Edit Field'</Text>
            </Label>
            <NumericEditField name='EditField'>
                <Position>[192 429 100 22]</Position>
            </NumericEditField>
            <Label name='SpinnerLabel'>
                <HorizontalAlignment>'right'</HorizontalAlignment>
                <Position>[121 389 47 22]</Position>
                <Text>'Spinner'</Text>
            </Label>
            <Spinner name='Spinner'>
                <Limits>[0 10]</Limits>
                <Position>[183 389 100 22]</Position>
                <RoundFractionalValues>'on'</RoundFractionalValues>
                <Value>4</Value>
            </Spinner>
            <Label name='DropDownLabel'>
                <HorizontalAlignment>'right'</HorizontalAlignment>
                <Position>[121 349 66 22]</Position>
                <Text>'Drop Down'</Text>
            </Label>
            <DropDown name='DropDown'>
                <Items>{'Add', 'Multiply'}</Items>
                <Position>[202 349 100 22]</Position>
                <Value>'Add'</Value>
            </DropDown>
            <CheckBox name='DoubleCheckBox'>
                <Position>[121 309 84 22]</Position>
                <Text>'Double it'</Text>
            </CheckBox>
            <Label name='DatePickerLabel'>
                <HorizontalAlignment>'right'</HorizontalAlignment>
                <Position>[121 269 68 22]</Position>
                <Text>'Date Picker'</Text>
            </Label>
            <DatePicker name='DatePicker'>
                <DisabledDates>[datetime([2026 12 25]); datetime([2027 1 1])]</DisabledDates>
                <Position>[204 269 150 22]</Position>
            </DatePicker>
            <CheckBoxTree name='Tree'>
                <CheckedNodes>[Node2]</CheckedNodes>
                <Position>[381 151 150 300]</Position>
                <Children>
                    <TreeNode name='Node'>
                        <Text>'Node'</Text>
                        <Children>
                            <TreeNode name='Node2'>
                                <Text>'Node2'</Text>
                            </TreeNode>
                            <TreeNode name='Node3'>
                                <Text>'Node3'</Text>
                            </TreeNode>
                            <TreeNode name='Node4'>
                                <Text>'Node4'</Text>
                            </TreeNode>
                        </Children>
                    </TreeNode>
                </Children>
            </CheckBoxTree>
            <Button name='ComputeButton'>
                <ButtonPushedFcn>ComputeButtonPushed</ButtonPushedFcn>
                <Position>[121 219 100 22]</Position>
                <Text>'Compute'</Text>
            </Button>
            <Label name='ResultLabel'>
                <Position>[121 179 31 22]</Position>
                <Text>'-'</Text>
            </Label>
        </Children>
    </UIFigure>
</Components>
%}

%---
%[app:runConfiguration]
%{
<?xml version='1.0' encoding='UTF-8'?>
<RunConfiguration>
    <StartupFcn>startupFcn</StartupFcn>
</RunConfiguration>
%}

%---
%[app:appDetails]
%{
<?xml version='1.0' encoding='UTF-8'?>
<AppDetails>
    <Name>AcceptFormPlain</Name>
    <Version>1.0</Version>
</AppDetails>
%}

%---
%[app:internalData]
%{
<?xml version='1.0' encoding='UTF-8'?>
<InternalData>
    <AppId>4caa76ce-c63e-42ba-9889-05fe82cd083b</AppId>
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