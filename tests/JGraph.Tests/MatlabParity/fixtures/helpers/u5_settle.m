function p = u5_settle(h, was)
% Waits for a grid to have laid a child out (U5 fixtures). R2025b lays an invisible uifigure out
% some tenths of a second after drawnow, so this waits for the child's Position to leave what it
% was (was), and then for it to stand still for two looks. An engine that lays out at once is
% through after one look.
t = tic;
while toc(t) < 10 && isequal(get(h, 'Position'), was)
    drawnow;
    pause(0.1);
end
p = get(h, 'Position');
same = 0;
while toc(t) < 15 && same < 2
    drawnow;
    pause(0.1);
    q = get(h, 'Position');
    if isequal(q, p)
        same = same + 1;
    else
        same = 0;
        p = q;
    end
end
end
