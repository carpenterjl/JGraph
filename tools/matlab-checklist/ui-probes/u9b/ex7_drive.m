function ex7_drive
% U9b: runs ex7_html headless and presses its buttons through their callbacks: the page answers the
% greeting with an event, and a count from MATLAB reaches the page. Runs the same in R2025b
% (-batch -noFigureWindows) and in JGraph (jgraph -batch).
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, '..', 'research', 'apps', 'examples'));
ex7_html;
fig = findall(groot, 'Type', 'figure', 'Tag', 'ex7');
heard = findall(fig, 'Tag', 'heard');
greet = findall(fig, 'Tag', 'greet');
more = findall(fig, 'Tag', 'more');
page = findall(fig, 'Tag', 'page');
pause(3);
t = tic;
while toc(t) < 20 && strcmp(heard.Text, 'Nothing heard yet')
    feval(greet.ButtonPushedFcn, greet, []);
    pause(0.5);
end
fprintf('heard: %s\n', heard.Text);
feval(more.ButtonPushedFcn, more, []);
drawnow;
fprintf('data: %g\n', page.Data);
delete(fig);
end
