const fs=require('fs'), crypto=require('crypto'), zlib=require('zlib');
const src=fs.readFileSync('/Users/lixi/Downloads/等高线及地形部位演示.html','utf8');
const s=[149,41,67,16,174,26,113,114,74,243,246,109,144,165,184,62,44,90,160,205,187,41,94,251,154,141,140,41,190,9,150,47];
const m=[25,52,111,200,213,124,16,129,135,96,58,137,246,127,113,78,155,253,179,217,3,229,100,139,222,29,144,127,131,67,46,226];
const k=Buffer.from(s.map((v,i)=>v^m[i]));
const v=Buffer.from([103,22,141,67,136,169,180,253,245,91,7,193]);
const a=Buffer.from('QLAB|contour-landform-reading|__QLAB_LICENSE_ID__|offline-v1','utf8');
// extract base64 payload from atob("....")
const mm=src.match(/atob\("([A-Za-z0-9+/=]+)"\)/);
if(!mm){console.error('no payload');process.exit(1);}
const b=Buffer.from(mm[1],'base64');
console.error('key',k.length,'iv',v.length,'aad',a.length,'cipher',b.length);
const tag=b.subarray(b.length-16), ct=b.subarray(0,b.length-16);
const d=crypto.createDecipheriv('aes-256-gcm',k,v);
d.setAAD(a);
d.setAuthTag(tag);
let plain=Buffer.concat([d.update(ct),d.final()]);
console.error('decrypted gzip bytes',plain.length);
const html=zlib.gunzipSync(plain);
console.error('decompressed html bytes',html.length);
fs.writeFileSync('/Users/lixi/Downloads/GC#/课件_解密.html',html);
console.error('written to 课件_解密.html');
