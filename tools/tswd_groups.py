"""Show the proc-group distribution (App / class / runtime) the way the GUI pulldown will."""
import sys, struct, re, collections
def u32(b,o): return struct.unpack_from('<I',b,o)[0]
def u16(b,o): return struct.unpack_from('<H',b,o)[0]
def find(path):
    d=open(path,'rb').read(); lfa=u32(d,0x3c); nsec=u16(d,lfa+6); optsz=u16(d,lfa+20); st=lfa+24+optsz
    text=(0x1000,0x50000)
    for i in range(nsec):
        o=st+i*40; nm=d[o:o+8].rstrip(b'\0')
        if nm==b'.text': text=(u32(d,o+12),u32(d,o+8))
    blob=None
    for i in range(nsec):
        o=st+i*40
        if d[o:o+8].rstrip(b'\0')==b'.cwdebug':
            rp=u32(d,o+20); loc=d[rp:rp+32]; blob=d[u32(loc,24):u32(loc,24)+u32(loc,16)]
    return blob, text
def cstr(b,o):
    try: e=b.index(0,o); return b[o:e].decode('latin1')
    except: return '?'
blob,(tva,tsz)=find(sys.argv[1])
dirv=[u32(blob,8+4*i) for i in range(12)]
NB=dirv[6]; SB=dirv[8]; AM=dirv[11]; nameSz=SB-NB

def group(name):
    if name.startswith('__') or '$$$' in name or name.startswith('R$') or '@_' in name: return '(runtime)'
    i=name.find('@F')
    if i<0: return '(runtime)'
    rest=name[i+2:]
    if not rest or not rest[0].isdigit(): return 'App'
    m=re.match(r'(\d+)',rest); L=int(m.group(1)); d=len(m.group(1))
    if d+L<=len(rest): return rest[d:d+L]
    return '(runtime)'

seen=set(); groups=collections.Counter()
o=SB
while o<AM:
    if blob[o]==0x05:
        nameOff=u32(blob,o+5); entry=u32(blob,o+9); lc=u32(blob,o+21)
        if 0<=nameOff<nameSz and tva<=entry<tva+tsz and 0<=lc<2000:
            nm=cstr(blob,NB+nameOff)
            if nm and nm[0]!='?' and all(32<=ord(c)<127 for c in nm) and entry not in seen:
                seen.add(entry); groups[group(nm)]+=1
    o+=1
print(f"total procs: {sum(groups.values())}   distinct groups: {len(groups)}")
for g,c in groups.most_common():
    print(f"   {c:4d}  {g}")
