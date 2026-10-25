function t = k9slow(seconds)
%K9SLOW Keep busy for the seconds asked, and say how long it took.
t0 = tic;
while toc(t0) < seconds
    pause(0.01);
end
t = round(toc(t0));
end
