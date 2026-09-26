function ip_write(path, text)
% IP_WRITE  Write text to path, creating its folders.
folder = fileparts(path);
if ~isfolder(folder), mkdir(folder); end
fid = fopen(path, 'w');
fprintf(fid, '%s\n', text);
fclose(fid);
end
