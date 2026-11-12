import math
def Phi(x): return 0.5*math.erfc(-x/math.sqrt(2))
def bs(cp,S,K,T,r,q,v):
    d1=(math.log(S/K)+(r-q+0.5*v*v)*T)/(v*math.sqrt(T)); d2=d1-v*math.sqrt(T)
    if cp=='c': return S*math.exp(-q*T)*Phi(d1)-K*math.exp(-r*T)*Phi(d2)
    return K*math.exp(-r*T)*Phi(-d2)-S*math.exp(-q*T)*Phi(-d1)
print("hull c", repr(bs('c',42,40,0.5,0.1,0,0.2)), "p", repr(bs('p',42,40,0.5,0.1,0,0.2)))
print("haug p", repr(bs('p',100,95,0.5,0.1,0.05,0.2)))
print("phi", repr(Phi(1.0)), repr(Phi(-1.96)), repr(Phi(3.0)), repr(Phi(-10.0)), repr(Phi(-30.0)))
n=60
x=[100+10*math.sin(i/5)+0.3*i for i in range(n)]
h=[v+1+abs(math.sin(i)) for i,v in enumerate(x)]
l=[v-1-abs(math.cos(i)) for i,v in enumerate(x)]
def ema(s,p):
    out=[float('nan')]*len(s); a=2/(p+1); v=sum(s[:p])/p; out[p-1]=v
    for i in range(p,len(s)): v=v+a*(s[i]-v); out[i]=v
    return out
def rsi(s,p):
    out=[float('nan')]*len(s); ch=[s[i]-s[i-1] for i in range(1,len(s))]
    g=sum(max(c,0) for c in ch[:p])/p; lo=sum(max(-c,0) for c in ch[:p])/p; out[p]=100*g/(g+lo)
    for i in range(p+1,len(s)):
        c=ch[i-1]; g=(g*(p-1)+max(c,0))/p; lo=(lo*(p-1)+max(-c,0))/p; out[i]=100*g/(g+lo)
    return out
def atr(h,l,c,p):
    tr=[None]+[max(h[i]-l[i],abs(h[i]-c[i-1]),abs(l[i]-c[i-1])) for i in range(1,len(c))]
    out=[float('nan')]*len(c); a=sum(tr[1:p+1])/p; out[p]=a
    for i in range(p+1,len(c)): a=(a*(p-1)+tr[i])/p; out[i]=a
    return out
e12=ema(x,12); e26=ema(x,26); m=[a-b for a,b in zip(e12,e26)]
sig=[float('nan')]*n; mm=m[25:]; s9=ema(mm,9)
for i,v in enumerate(s9): sig[25+i]=v
k=50.0; d=50.0; K=[]; D=[]; J=[]
for i in range(n):
    if i<8: K.append(float('nan')); D.append(float('nan')); J.append(float('nan')); continue
    hh=max(h[i-8:i+1]); ll=min(l[i-8:i+1]); rsv=100*(x[i]-ll)/(hh-ll)
    k=(2*k+rsv)/3; d=(2*d+k)/3; K.append(k); D.append(d); J.append(3*k-2*d)
w=x[40:60]; mu=sum(w)/20; sd=math.sqrt(sum((v-mu)**2 for v in w)/20)
for i in (11,30,59): print("ema12",i,repr(e12[i]))
for i in (14,30,59): print("rsi14",i,repr(rsi(x,14)[i]))
for i in (25,40,59): print("macd",i,repr(m[i]))
for i in (33,59): print("signal",i,repr(sig[i]))
for i in (14,59): print("atr14",i,repr(atr(h,l,x,14)[i]))
for i in (8,20,59): print("kdj",i,repr(K[i]),repr(D[i]),repr(J[i]))
print("boll59",repr(mu),repr(mu+2*sd),repr(mu-2*sd))