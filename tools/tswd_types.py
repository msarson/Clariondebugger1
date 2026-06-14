"""Recursive TSWD type decoder. Reads the type tag at typeRef+4 and decodes."""
import sys, struct
def u32(b,o): return struct.unpack_from('<I',b,o)[0]
def i32(b,o): return struct.unpack_from('<i',b,o)[0]
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
dirv=[u32(blob,8+4*i) for i in range(12)]
NB=dirv[6]; SB=dirv[8]   # name base, symbol-stream base (blob-relative)

def raw(tr,n=28):
    o=SB+tr; return ' '.join(f'{x:02x}' for x in blob[o:o+n])

def name_at(noff):
    try: return cstr(blob,NB+noff)
    except: return f'?{noff:#x}'

def decode(tr, depth=0):
    """decode the type record whose tag lives at SB+tr+4."""
    if tr<0 or SB+tr+5>len(blob) or depth>6: return "?"
    o=SB+tr+4
    tag=blob[o]
    if tag in (0x11,0x12,0x13,0x14):
        size=u32(blob,o+1)
        kind={0x11:'int',0x12:'uint',0x13:'float',0x14:'char'}[tag]
        return f"{kind}{size*8}" if tag!=0x14 else "char"
    if tag==0x23:
        size=u32(blob,o+1); places=blob[o+5]; return f"decimal(size={size},places={places})"
    if tag==0x24:
        size=u32(blob,o+1); places=blob[o+5]; return f"pdecimal(size={size},places={places})"
    if tag==0x08:
        size=u32(blob,o+1); cnt=u32(blob,o+5)
        members=[u32(blob,o+9+4*i) for i in range(cnt)]
        inner=', '.join(decode_member(m,depth+1) for m in members)
        return f"GROUP(size={size}){{{inner}}}"
    if tag==0x18:
        # array/string descriptor: 0x18 | refA | refB | elem(tag+u32 size) | 0x0f | nameref | numDims | length
        elem_o=o+1+8                      # skip refA, refB
        etag=blob[elem_o]; esize=u32(blob,elem_o+1)
        p=elem_o+5
        if blob[p]==0x0f:
            length=u32(blob,p+1+8)        # 0x0f | nameref(u32) | numDims(u32) | length(u32)
            if etag==0x14: return f"STRING({length})"
            return f"ARRAY[{length}] of {decode_inline(etag,esize)}"
        return f"COMPOSITE raw={raw(tr,32)}"
    return f"tag={tag:#x} raw={raw(tr,24)}"

def decode_inline(tag,size):
    return {0x11:f'int{size*8}',0x12:f'uint{size*8}',0x13:f'float{size*8}',0x14:'char'}.get(tag,f'tag{tag:#x}')

def decode_member(tr, depth):
    """a group member: variable record (tag 0x04) -> name + type."""
    o=SB+tr
    if blob[o]==0x04:
        typeRef=u32(blob,o+1); nameOff=u32(blob,o+5); off=i32(blob,o+9)
        return f"{name_at(nameOff)}@{off}:{decode(typeRef,depth)}"
    return f"member@{tr:#x} raw={raw(tr)}"

# walk all tag-0x04 variable records
print("=== decoded variables ===")
o=SB; end=dirv[11]
while o+13<=end:
    if blob[o]==0x04:
        typeRef=u32(blob,o+1); nameOff=u32(blob,o+5); off=i32(blob,o+9)
        if nameOff < (SB-NB):
            nm=name_at(nameOff)
            loc=f"rva={0x400000+off:#x}" if off>0x1000 else f"frame={off}"
            print(f"  {nm:10s} {loc:16s} {decode(typeRef)}")
            o+=13; continue
    o+=1
