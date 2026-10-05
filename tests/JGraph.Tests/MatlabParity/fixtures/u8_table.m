% record: -noFigureWindows
% U8 of the app-building plan (ADR 0206): how a uitable behaves - what its Data may be and reads
% back as, what its names and its column properties become, its selection against its data, and
% what its other properties do to one another - in a classic figure (F) and a uifigure (U).
for q = 1:2
    if q == 1
        p = figure('Visible', 'off', 'Position', [100 100 560 420]); w = 'F';
    else
        p = uifigure('Visible', 'off', 'Position', [100 100 560 420]); w = 'U';
    end
    T = table([1; 2], {'a'; 'b'}, [true; false], 'VariableNames', {'N', 'S', 'L'});
    R = table([1; 2], 'RowNames', {'r1', 'r2'});
    datas = {'magic', magic(3); 'cell', {1, 'a', true; 2, 'b', false}; 'logical', [true false; false true]; 'table', T; ...
        'string', ["a" "b"; "c" "d"]; 'single', single([1.5 2.5]); 'int8', int8([1 2; 3 4]); 'char', 'abc'; 'cellstr', {'a', 'b'}; ...
        'empty', []; 'zeros03', zeros(0, 3); 'nested', {1, {2}}; 'cellvector', {1, [1 2]}; 'struct', struct('a', 1); ...
        'complex', [1+2i 3]; 'nan', [NaN Inf -Inf]; 'handle', {@sin}; 'column', (1:4)'; 'rows', R; 'cellempty', {[], 1}; ...
        'cellint', {int8(1), single(2)}; 'cellstring', {"s", 1}; 'stringscalar', "s"; 'emptycell', {}; 'sparse', sparse([1 0; 0 1])};
    for k = 1:size(datas, 1)
        u2_chk([w '_data_' datas{k, 1}], @() u8_data(uitable(p, 'Data', datas{k, 2})));
        delete(allchild(p));
    end

    % names
    u2_chk([w '_columnname_with_table'], @() u5_text(get(uitable(p, 'Data', T), 'ColumnName')));
    u2_chk([w '_rowname_with_table'], @() u5_text(get(uitable(p, 'Data', T), 'RowName')));
    u2_chk([w '_rowname_with_row_names'], @() u5_text(get(uitable(p, 'Data', R), 'RowName')));
    u2_chk([w '_columnname_fewer'], @() u5_text(get(uitable(p, 'Data', magic(3), 'ColumnName', {'A', 'B'}), 'ColumnName')));
    u2_chk([w '_columnname_row_cell'], @() u5_text(get(uitable(p, 'ColumnName', {'A', 'B', 'C'}), 'ColumnName')));
    u2_chk([w '_columnname_string_row'], @() u5_text(get(uitable(p, 'ColumnName', ["A" "B"]), 'ColumnName')));
    u2_chk([w '_columnname_numbers'], @() u5_text(get(uitable(p, 'ColumnName', {1, 2}), 'ColumnName')));
    u2_chk([w '_columnname_empty_cell'], @() u5_text(get(uitable(p, 'ColumnName', {}), 'ColumnName')));
    u2_chk([w '_columnname_empty_in_cell'], @() u5_text(get(uitable(p, 'ColumnName', {'a', [], 'c'}), 'ColumnName')));
    u2_chk([w '_columnname_2d_cell'], @() u5_text(get(uitable(p, 'ColumnName', {'a', 'b'; 'c', 'd'}), 'ColumnName')));
    u2_chk([w '_columnname_nested'], @() u5_text(get(uitable(p, 'ColumnName', {'a', {'b'}}), 'ColumnName')));
    u2_chk([w '_columnname_vector'], @() u1_show(get(uitable(p, 'ColumnName', [1 10 100]), 'ColumnName')));
    u2_chk([w '_columnname_matrix'], @() u5_text(get(uitable(p, 'ColumnName', magic(2)), 'ColumnName')));
    u2_chk([w '_columnname_caps'], @() u5_text(get(uitable(p, 'ColumnName', 'Numbered'), 'ColumnName')));
    u2_chk([w '_columnname_back'], @() u5_text(u8_setget(u8_setget2(uitable(p), 'ColumnName', {'a'}), 'ColumnName', 'numbered')));
    u2_chk([w '_rowname_column_cell'], @() u5_text(get(uitable(p, 'RowName', {'r1'; 'r2'}), 'RowName')));
    u2_chk([w '_rowname_number'], @() u5_text(get(uitable(p, 'RowName', 2.5), 'RowName')));
    u2_chk([w '_rowname_string_column'], @() u5_text(get(uitable(p, 'RowName', ["a"; "b"]), 'RowName')));
    delete(allchild(p));

    % columns
    u2_chk([w '_editable_scalar'], @() u5_text(get(uitable(p, 'ColumnEditable', true), 'ColumnEditable')));
    u2_chk([w '_editable_row'], @() u5_text(get(uitable(p, 'ColumnEditable', [true false true]), 'ColumnEditable')));
    u2_chk([w '_editable_column'], @() u5_text(get(uitable(p, 'ColumnEditable', [true; false]), 'ColumnEditable')));
    u2_chk([w '_editable_numbers'], @() u5_text(get(uitable(p, 'ColumnEditable', [1 0 1]), 'ColumnEditable')));
    u2_chk([w '_sortable_row'], @() u5_text(get(uitable(p, 'ColumnSortable', [true false]), 'ColumnSortable')));
    u2_chk([w '_sortable_column'], @() u5_text(get(uitable(p, 'ColumnSortable', [true; false]), 'ColumnSortable')));
    formats = {'char', 'numeric', 'logical', 'short', 'long', 'shortE', 'longE', 'shortG', 'longG', 'shortEng', 'longEng', 'bank', '+', 'rat', ...
        'short e', 'NUMERIC', 'hex', 'bogus', ''};
    for k = 1:numel(formats)
        u2_chk(sprintf('%s_format_%02d', w, k), @() u5_text(get(uitable(p, 'ColumnFormat', formats(k)), 'ColumnFormat')));
    end
    u2_chk([w '_format_empty_in_cell'], @() u5_text(get(uitable(p, 'ColumnFormat', {[]}), 'ColumnFormat')));
    u2_chk([w '_format_choices'], @() u5_text(get(uitable(p, 'ColumnFormat', {{'a', 'b'}, 'numeric'}), 'ColumnFormat')));
    u2_chk([w '_format_choices_column'], @() u5_text(get(uitable(p, 'ColumnFormat', {{'a'; 'b'}}), 'ColumnFormat')));
    u2_chk([w '_format_choices_empty'], @() u5_text(get(uitable(p, 'ColumnFormat', {{}}), 'ColumnFormat')));
    u2_chk([w '_format_choices_numbers'], @() u5_text(get(uitable(p, 'ColumnFormat', {{1, 2}}), 'ColumnFormat')));
    u2_chk([w '_format_column'], @() u5_text(get(uitable(p, 'ColumnFormat', {'numeric'; 'char'}), 'ColumnFormat')));
    u2_chk([w '_format_strings'], @() u5_text(get(uitable(p, 'ColumnFormat', ["numeric" "char"]), 'ColumnFormat')));
    u2_chk([w '_format_string_in_cell'], @() u5_text(get(uitable(p, 'ColumnFormat', {"char"}), 'ColumnFormat')));
    u2_chk([w '_format_number'], @() u5_text(get(uitable(p, 'ColumnFormat', {5}), 'ColumnFormat')));
    u2_chk([w '_format_char'], @() u5_text(get(uitable(p, 'ColumnFormat', 'numeric'), 'ColumnFormat')));
    widths = {'auto', 'fit', '1x', {'auto'}, {50}, {50, 'auto'}, {'1x', '2x'}, {'fit', 50, '2x', 'auto'}, 50, [50 60], {-1}, {0}, {'0x'}, {'x'}, ...
        {50; 60}, {1.5}, {'1.5x'}, {int8(5)}, {"auto"}, "auto", ["auto" "fit"], {}, 'AUTO', {'2X'}, {Inf}, {NaN}, {[1 2]}, {true}};
    for k = 1:numel(widths)
        u2_chk(sprintf('%s_width_%02d', w, k), @() u5_text(get(uitable(p, 'ColumnWidth', widths{k}), 'ColumnWidth')));
    end
    colors = {[1 0 0], [1 0 0; 0 1 0; 0 0 1], 'red', {'red', 'blue'}, ["red" "blue"], [1 1 1 1], 'none', '#ff0000', zeros(0, 3), {'red'; '#00f'}, {'red', 5}};
    for k = 1:numel(colors)
        u2_chk(sprintf('%s_stripes_%02d', w, k), @() u5_text(get(uitable(p, 'BackgroundColor', colors{k}), 'BackgroundColor')));
    end
    delete(allchild(p));

    % selection
    u2_chk([w '_selection_default'], @() u5_text(get(uitable(p, 'Data', magic(3)), 'Selection')));
    u2_chk([w '_selection_cell'], @() u5_text(get(uitable(p, 'Data', magic(3), 'Selection', [1 2]), 'Selection')));
    u2_chk([w '_selection_cells'], @() u5_text(get(uitable(p, 'Data', magic(3), 'Selection', [1 2; 2 3]), 'Selection')));
    u2_chk([w '_selection_outside'], @() u5_text(get(uitable(p, 'Data', magic(3), 'Selection', [9 9]), 'Selection')));
    u2_chk([w '_selection_three'], @() u5_text(get(uitable(p, 'Data', magic(3), 'Selection', [1 2 3]), 'Selection')));
    u2_chk([w '_selection_rows'], @() u5_text(get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', [3 1]), 'Selection')));
    u2_chk([w '_selection_rows_column'], @() u5_text(get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', [1; 3]), 'Selection')));
    u2_chk([w '_selection_column'], @() u5_text(get(uitable(p, 'Data', magic(3), 'SelectionType', 'column', 'Selection', 2), 'Selection')));
    u2_chk([w '_selection_columns_column'], @() u5_text(get(uitable(p, 'Data', magic(3), 'SelectionType', 'column', 'Selection', [1; 2]), 'Selection')));
    u2_chk([w '_selection_row_zero'], @() u5_text(get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', 0), 'Selection')));
    u2_chk([w '_selection_row_fraction'], @() u5_text(get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', 1.5), 'Selection')));
    u2_chk([w '_selection_single'], @() u5_text(get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Multiselect', 'off', 'Selection', [1 3]), 'Selection')));
    u2_chk([w '_selection_single_cell'], @() u5_text(get(uitable(p, 'Data', magic(3), 'Multiselect', 'off', 'Selection', [1 3]), 'Selection')));
    u2_chk([w '_selection_no_data'], @() u5_text(get(uitable(p, 'Selection', [1 1]), 'Selection')));
    u2_chk([w '_selection_after_type'], @() u5_text(u8_setget(uitable(p, 'Data', magic(3), 'Selection', [1 2]), 'SelectionType', 'row', 'Selection')));
    u2_chk([w '_selection_after_data'], @() u5_text(u8_setget(uitable(p, 'Data', magic(3), 'Selection', [1 2]), 'Data', magic(2), 'Selection')));
    u2_chk([w '_selection_cleared'], @() u5_text(u8_setget(uitable(p, 'Data', magic(3), 'Selection', [1 2]), 'Selection', [])));
    delete(allchild(p));

    % the rest
    t = uitable(p, 'Data', magic(3));
    t.Data(2, 2) = 99;
    fprintf('CHK|%s_indexed_write|%s|exact\n', w, mat2str(t.Data));
    t.Data(:, 4) = 1;
    fprintf('CHK|%s_grown|%s|exact\n', w, mat2str(size(t.Data)));
    c = uitable(p, 'Data', {1, 'a'; 2, 'b'});
    c.Data{2, 2} = 'x';
    fprintf('CHK|%s_cell_write|%s|exact\n', w, u5_text(c.Data));
    fprintf('CHK|%s_display_data|%s|exact\n', w, mat2str(get(t, 'DisplayData')));
    u2_chk([w '_units_normalized'], @() u8_setget(uitable(p), 'Units', 'normalized', 'Position'));
    u2_chk([w '_font_units'], @() u8_font(uitable(p)));
    u2_chk([w '_font_normalized'], @() get(uitable(p, 'FontUnits', 'normalized'), 'FontSize'));
    u2_chk([w '_font_weight_light'], @() get(uitable(p, 'FontWeight', 'light'), 'FontWeight'));
    u2_chk([w '_font_angle_oblique'], @() get(uitable(p, 'FontAngle', 'oblique'), 'FontAngle'));
    u2_chk([w '_enable_inactive'], @() get(uitable(p, 'Enable', 'inactive'), 'Enable'));
    u2_chk([w '_enable_true'], @() get(uitable(p, 'Enable', true), 'Enable'));
    u2_chk([w '_tooltipstring'], @() get(uitable(p, 'TooltipString', 'x'), 'Tooltip'));
    u2_chk([w '_rearrangeable_alias'], @() get(uitable(p, 'RearrangeableColumns', 'on'), 'ColumnRearrangeable'));
    u2_chk([w '_isprop'], @() double([isprop(t, 'Extent') isprop(t, 'Value') isprop(t, 'String') isprop(t, 'TooltipString') isprop(t, 'RearrangeableColumns')]));
    u2_chk([w '_extent'], @() get(uitable(p), 'Extent'));
    u2_err([w '_set_extent'], @() set(uitable(p), 'Extent', [0 0 1 1]));
    u2_err([w '_set_displaydata'], @() set(uitable(p), 'DisplayData', 1));
    u2_err([w '_set_styleconfigurations'], @() set(uitable(p), 'StyleConfigurations', 1));
    u2_chk([w '_in_panel'], @() get(uitable(uipanel(p)), 'Position'));
    u2_chk([w '_inner_position'], @() get(uitable(p, 'InnerPosition', [1 2 30 40]), 'Position'));
    u2_chk([w '_outer_position'], @() get(uitable(p, 'OuterPosition', [1 2 30 40]), 'Position'));
    u2_chk([w '_position_negative'], @() get(uitable(p, 'Position', [1 2 -3 4]), 'Position'));
    u2_chk([w '_callback_forms'], @() u8_callbacks(uitable(p), 'CellEditCallback'));
    u2_chk([w '_children'], @() isempty(get(uitable(p), 'Children')));
    delete(p);
end
