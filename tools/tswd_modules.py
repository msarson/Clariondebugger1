import sys, struct
def u32(b,o): return struct.unpack_from('<I',b,o)[0]
def u16(b,o): return struct.unpack_from('<H',b,o)[0]
def find_blob(path):
    d=open(path,'rb').read(); lfa=u32(d,0x3c); nsec=u16(d,lfa+6); optsz=u16(d,lfa+20); st=lfa+24+optsz
    for i in range(nsec):
        o=st+i*40
        if d[o:o+8].rstrip(b'\0')==b'.cwdebug':
            rp=u32(d,o+20); loc=d[rp:rp+32]; return d[u32(loc,24):u32(loc,24)+u32(loc,16)]
def cstr(b,o):
    e=b.index(0,o); return b[o:e].decode('latin1')

blob=find_blob(sys.argv[1])
modCount=u32(blob,4)
dirv=[u32(blob,8+4*i) for i in range(12)]
nameArr=dirv[0]; nameTbl=dirv[1]; perMod=dirv[2]; ltOff=dirv[3]; ltCnt=dirv[4]
print(f"modules={modCount} nameArr={nameArr:#x} nameTbl={nameTbl:#x} perMod={perMod:#x} ltOff={ltOff:#x} ltCnt={ltCnt}")

names=[cstr(blob, nameTbl + u32(blob, nameArr+4*i)) for i in range(modCount)]

# per-module 8-byte records
print("\nmod  name              perMod[A]   perMod[B]")
recs=[]
for i in range(modCount):
    A=u32(blob, perMod+i*8); B=u32(blob, perMod+i*8+4); recs.append((A,B))
    print(f"{i:3d}  {names[i]:16s}  {A:#010x}  {B:#010x}")

# empirical line-table module boundaries: rva drops
print("\n=== line-table segments (rva resets) ===")
prev=0; segstart=0; segs=[]
for i in range(ltCnt):
    o=ltOff+i*6
    if o+6>len(blob): break
    line=u16(blob,o); rva=u32(blob,o+2)
    if i>0 and rva < prev - 0x2000:   # significant backward jump
        segs.append((segstart,i))
        segstart=i
    prev=rva
segs.append((segstart, ltCnt))
print(f"segments found: {len(segs)}")
for k,(s,e) in enumerate(segs[:8]):
    o=ltOff+s*6
    print(f"  seg {k}: entries {s}..{e} ({e-s})  firstline={u16(blob,o)} firstrva={u32(blob,o+2):#x}")
