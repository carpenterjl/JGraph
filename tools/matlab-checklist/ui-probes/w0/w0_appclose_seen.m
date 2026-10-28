function w0_appclose_seen(src, event)
% The AppCloseDataFcn w0_appclose sets: records what reaches it.
global W0_CLOSE %#ok<GVMIS>
props = '';
try, props = strjoin(properties(event)', ','); catch, end %#ok<CTCH>
W0_CLOSE{end + 1} = sprintf('src %s, event %s (%s), EventName %s, App is src %d', class(src), class(event), ...
    props, char(event.EventName), isequal(event.App, src));
end
