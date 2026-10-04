import zipfile,sys,os
src,dst=sys.argv[1],sys.argv[2]
z=zipfile.ZipFile(src)
for i in z.infolist(): print(f"{i.file_size:9d} {i.compress_type} {i.filename}")
z.extractall(dst)
