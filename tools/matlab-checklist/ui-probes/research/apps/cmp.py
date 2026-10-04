import zipfile,re,sys
z=zipfile.ZipFile(sys.argv[1]); d=z.read("matlab/document.xml").decode()
code=re.search(r"<!\[CDATA\[(.*)\]\]>",d,re.S).group(1)
e=open(sys.argv[2],encoding="utf8").read().replace("\r\n","\n")
code=code.replace("\r\n","\n")
print(len(code),len(e), code==e)
import difflib
for l in list(difflib.unified_diff(code.splitlines(),e.splitlines(),lineterm=""))[:40]: print(l)
