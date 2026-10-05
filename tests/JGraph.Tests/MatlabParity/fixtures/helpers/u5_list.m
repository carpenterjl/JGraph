function s = u5_list(d)
% A drop-down or a list box on one line: Value, ValueIndex, Items and ItemsData (U5 fixtures).
s = ['Value=' u5_text(get(d, 'Value')) ' Index=' u5_text(get(d, 'ValueIndex')) ' Items=' u5_text(get(d, 'Items')) ...
    ' Data=' u5_text(get(d, 'ItemsData'))];
end
