namespace AvaloniaKit.Resources;

// ══════════════════════════════════════════════════════════════════════════════
//  MapHtml — 地图页面（一份 HTML，四端复用；仿高德地图）
//  · 地图与路线：高德 JS API v2.0（AMap.Driving/Walking/Riding/Transfer）
//  · 查路线：多策略并行算路（最快/最短/躲避拥堵）去重 → 多条备选路线，手动绘制多条
//    polyline，点线或点卡片切换选中（选中高亮蓝、其余置灰）
//  · 导航：选中路线后「开始导航」→ 车辆沿路推进 + 地图跟随 + 转向卡 + 剩余里程/预计到达
//    + 按所选语音包 TTS 逐路口播报；「退出」回到路线选择
//  · 切换语音包：SpeechSynthesis(TTS) 用音色/语速/音调模拟 5 个语音包
//  · JS→宿主消息：app://voice?id=（切语音包持久化）、app://nav?open=1/0（导航态，
//    宿主据此让返回键先退导航）；原生端 app:// 导航拦截 / Browser postMessage 'map-msg:'
//  · 凭证唯一修改点：下方 AmapKey / AmapSecurity 两个常量
// ══════════════════════════════════════════════════════════════════════════════
public static class MapHtml
{
    // ★ 高德开放平台「Web 端 (JS API)」凭证（唯一修改点）；域名白名单须留空
    private const string AmapKey = "4b91b6eb0921d53a23de8495a63c1702";
    private const string AmapSecurity = "94f892d941eb8151ef305a24d46a2d82";

    /// <summary>按初始语音包生成页面（voiceId：warm/lively/deep/calm/fast）</summary>
    public static string Build(string voiceId)
        => Template.Replace("__AMAP_KEY__", AmapKey)
                   .Replace("__AMAP_SECURITY__", AmapSecurity)
                   .Replace("__INIT_VOICE__", string.IsNullOrWhiteSpace(voiceId) ? "warm" : voiceId);

    private const string Template = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
<style>
  * { margin:0; padding:0; box-sizing:border-box; -webkit-tap-highlight-color:transparent; }
  html,body { height:100%; font-family:-apple-system,"PingFang SC","Microsoft YaHei",sans-serif; }
  #map { position:absolute; inset:0; background:#e6e8eb; }

  .topbar { position:absolute; left:10px; right:10px; top:10px; z-index:20;
    background:#fff; border-radius:14px; padding:10px 12px; box-shadow:0 4px 16px rgba(0,0,0,.14); }
  .io { display:flex; align-items:center; height:40px; }
  .io + .io { border-top:1px solid #f0f0f0; }
  .dot { width:9px; height:9px; border-radius:50%; margin-right:10px; flex:none; }
  .dot.start { background:#1AAD6B; } .dot.end { background:#F5222D; }
  .io input { flex:1; border:none; outline:none; font-size:15px; background:transparent; color:#222; }
  #swap { position:absolute; right:14px; top:36px; width:30px; height:30px; border:none;
    background:#f4f6f8; border-radius:50%; color:#666; font-size:16px; }
  .loc-fab { position:absolute; right:12px; bottom:150px; z-index:21; height:38px; padding:0 15px;
    border:none; border-radius:20px; background:#fff; box-shadow:0 3px 10px rgba(0,0,0,.18);
    color:#1668e3; font-size:14px; font-weight:600; }
  .modes { display:flex; gap:6px; margin-top:10px; }
  .modes button { flex:1; height:32px; border:none; border-radius:8px; background:#f4f6f8; color:#555; font-size:14px; }
  .modes button.on { background:#eaf3ff; color:#1976ff; font-weight:600; }
  #go { width:100%; height:40px; margin-top:10px; border:none; border-radius:10px;
    background:#1976ff; color:#fff; font-size:15px; font-weight:600; }

  .voice-fab { position:absolute; left:12px; bottom:150px; z-index:21; height:38px; padding:0 15px;
    border:none; border-radius:20px; background:#fff; box-shadow:0 3px 10px rgba(0,0,0,.18);
    color:#1976ff; font-size:14px; font-weight:600; }

  /* 多路线卡片 + 开始导航 */
  .plan-panel { position:absolute; left:0; right:0; bottom:0; z-index:22; background:#fff;
    border-radius:16px 16px 0 0; box-shadow:0 -3px 16px rgba(0,0,0,.14); padding:12px 12px 14px; }
  .rcards { display:flex; gap:8px; overflow-x:auto; padding-bottom:2px; }
  .rcard { flex:0 0 auto; min-width:120px; padding:10px 13px; border-radius:12px;
    background:#f4f6f8; border:1.5px solid transparent; transition:all .15s; }
  .rcard.on { background:#eaf3ff; border-color:#1976ff; box-shadow:0 2px 8px rgba(25,118,255,.2); }
  .rcard .rt { font-size:18px; font-weight:800; color:#1976ff; }
  .rcard .rd { font-size:12px; color:#666; margin-top:3px; white-space:nowrap; }
  .rcard .rl { font-size:11px; color:#999; margin-top:4px; }
  .rcard.on .rl { color:#1976ff; font-weight:600; }
  .start-nav { width:100%; height:44px; margin-top:10px; border:none; border-radius:12px;
    background:#1976ff; color:#fff; font-size:16px; font-weight:700; }

  /* 导航视图 */
  .nav-top { position:absolute; left:10px; right:10px; top:10px; z-index:25; background:#1f6fe5;
    color:#fff; border-radius:14px; padding:12px 14px; display:flex; align-items:center; gap:14px;
    box-shadow:0 4px 16px rgba(0,0,0,.2); }
  .nav-top .turn-ico { font-size:34px; line-height:1; width:38px; text-align:center; }
  .nav-top .turn-dist { font-size:14px; opacity:.9; }
  .nav-top .turn-instr { font-size:18px; font-weight:700; margin-top:2px; }
  .nav-bottom { position:absolute; left:10px; right:10px; bottom:12px; z-index:25; background:#fff;
    border-radius:14px; padding:10px 12px; display:flex; align-items:center; box-shadow:0 4px 16px rgba(0,0,0,.16); }
  .nav-info { flex:1; } .nav-info b { font-size:18px; color:#1976ff; font-weight:800; }
  .nav-info span { display:block; font-size:12px; color:#888; margin-top:2px; }
  .exit-nav { border:none; background:#f24d4d; color:#fff; border-radius:10px; height:40px; padding:0 20px; font-size:15px; font-weight:600; }
  .car-dot { width:18px; height:18px; border-radius:50%; background:#1976ff; border:3px solid #fff;
    box-shadow:0 0 0 2px #1976ff,0 2px 6px rgba(0,0,0,.4); }

  .mask { position:absolute; inset:0; z-index:30; background:rgba(0,0,0,.35); }
  .sheet { position:absolute; left:0; right:0; bottom:0; z-index:31; background:#fff;
    border-radius:16px 16px 0 0; padding:16px; max-height:70%; overflow:auto; }
  .sheet h3 { font-size:16px; color:#222; margin-bottom:10px; }
  .pack { display:flex; align-items:center; justify-content:space-between; padding:13px 10px; border-radius:10px; }
  .pack.on { background:#eaf3ff; }
  .pack .pn { font-size:15px; color:#222; } .pack .pd { font-size:12px; color:#999; margin-top:2px; }
  .pack .chk { color:#1976ff; font-weight:700; font-size:16px; visibility:hidden; }
  .pack.on .chk { visibility:visible; }

  .toast { position:absolute; left:50%; top:50%; transform:translate(-50%,-50%); z-index:50;
    background:rgba(0,0,0,.82); color:#fff; padding:10px 16px; border-radius:10px; font-size:14px;
    opacity:0; transition:opacity .25s; pointer-events:none; max-width:80%; text-align:center; }
  .toast.show { opacity:1; }

  .fail { position:absolute; inset:0; z-index:40; background:#f5f6f8; display:flex; flex-direction:column;
    align-items:center; justify-content:center; gap:12px; }
  .fail .em { font-size:46px; } .fail .tx { color:#666; font-size:14px; text-align:center; padding:0 30px; line-height:1.7; }
  .hidden { display:none !important; }
</style>
</head>
<body>
<div id="map"></div>

<div class="topbar" id="topbar">
  <div class="io"><span class="dot start"></span><input id="from" placeholder="选择起点"></div>
  <div class="io"><span class="dot end"></span><input id="to" placeholder="输入终点"></div>
  <button id="swap" title="交换起终点">⇅</button>
  <div class="modes">
    <button data-m="drive" class="on">驾车</button>
    <button data-m="walk">步行</button>
    <button data-m="bus">公交</button>
    <button data-m="ride">骑行</button>
  </div>
  <button id="go">查路线</button>
</div>

<button class="voice-fab" id="voiceFab">🎙 <span id="voiceName">语音包</span></button>
<button class="loc-fab" id="loc" title="定位">◎ 定位</button>

<div class="plan-panel hidden" id="planPanel">
  <div class="rcards" id="routeCards"></div>
  <button class="start-nav" id="startNav">开始导航</button>
</div>

<div class="nav-top hidden" id="navTop">
  <div class="turn-ico" id="turnIco">↑</div>
  <div class="turn-main"><div class="turn-dist" id="turnDist"></div><div class="turn-instr" id="turnInstr">开始导航</div></div>
</div>
<div class="nav-bottom hidden" id="navBottom">
  <div class="nav-info"><b id="navRemain">--</b><span id="navEta"></span></div>
  <button class="exit-nav" id="exitNav">退出</button>
</div>

<div class="mask hidden" id="mask"></div>
<div class="sheet hidden" id="voiceSheet"><h3>选择语音包</h3><div id="packList"></div></div>

<div class="toast" id="toast"></div>
<div class="fail hidden" id="fail">
  <div class="em">🛰️</div>
  <div class="tx">地图加载失败：请检查网络，或确认高德 Key 已正确配置（Web 端 JS API + 安全密钥，且域名白名单留空）。</div>
</div>

<script>
window._AMapSecurityConfig = { securityJsCode: '__AMAP_SECURITY__' };
</script>
<script src="https://webapi.amap.com/maps?v=2.0&key=__AMAP_KEY__&plugin=AMap.Driving,AMap.Walking,AMap.Transfer,AMap.Riding,AMap.AutoComplete,AMap.Geolocation,AMap.CitySearch,AMap.Geocoder"></script>
<script>
(function(){
  'use strict';

  var PACKS = [
    { id:'warm',   name:'温柔女声', desc:'轻柔舒缓', rate:1.0,  pitch:1.35, sex:'female' },
    { id:'lively', name:'活力少女', desc:'元气满满', rate:1.15, pitch:1.6,  sex:'female' },
    { id:'deep',   name:'浑厚男声', desc:'沉稳大气', rate:0.95, pitch:0.7,  sex:'male'   },
    { id:'calm',   name:'标准播报', desc:'清晰自然', rate:1.0,  pitch:1.0,  sex:'any'    },
    { id:'fast',   name:'急速报路', desc:'语速偏快', rate:1.5,  pitch:1.05, sex:'any'    }
  ];
  var voiceId = '__INIT_VOICE__';
  function curPack(){ for(var i=0;i<PACKS.length;i++) if(PACKS[i].id===voiceId) return PACKS[i]; return PACKS[0]; }

  var $ = function(id){ return document.getElementById(id); };
  var toastTimer = 0;
  function toast(t){ var el=$('toast'); el.textContent=t; el.classList.add('show');
    clearTimeout(toastTimer); toastTimer=setTimeout(function(){ el.classList.remove('show'); }, 2200); }
  function pad(n){ return n<10 ? '0'+n : ''+n; }

  function sendHost(url){
    if (window.parent !== window){ try{ window.parent.postMessage('map-msg:'+url,'*'); }catch(e){} }
    else { location.href = url; }
  }

  // ── 语音合成 ──
  function pickVoice(sex){
    if(!('speechSynthesis' in window)) return null;
    var vs = speechSynthesis.getVoices() || [];
    var zh = vs.filter(function(v){ return /zh|cmn|Chinese/i.test((v.lang||'')+' '+(v.name||'')); });
    if(!zh.length) zh = vs;
    if(sex==='female'){ var f=zh.filter(function(v){ return /female|xiao|yao|hui|mei|婷|女|Ting|Yaoyao|Xiaoxiao/i.test(v.name); }); if(f.length) return f[0]; }
    if(sex==='male'){ var m=zh.filter(function(v){ return /male|kang|yun|云|康|男|Kangkang|Yunyang/i.test(v.name); }); if(m.length) return m[0]; }
    return zh[0] || null;
  }
  function speakLines(lines){
    if(!('speechSynthesis' in window)){ return; }
    speechSynthesis.cancel();
    var p = curPack();
    for(var i=0;i<lines.length;i++){
      var u = new SpeechSynthesisUtterance(lines[i]);
      u.lang='zh-CN'; u.rate=p.rate; u.pitch=p.pitch;
      var v = pickVoice(p.sex); if(v) u.voice=v;
      speechSynthesis.speak(u);
    }
  }

  // ── 地图 ──
  var map=null, mapReady=false, curMode='drive', myCity='北京', myPos=null, geocoder=null;
  var routes=[], overlays=[], selIdx=0;
  var nav={ on:false, timer:0, full:[], seg:[], suffix:[], ci:0, stepIdx:[], stepInstr:[], stepAction:[],
            announced:-1, totalDist:0, totalTime:0, carMk:null, lastBrg:-999 };

  function showFail(){ $('fail').classList.remove('hidden'); }

  function initMap(){
    if (typeof AMap === 'undefined'){ showFail(); return; }
    try{
      map = new AMap.Map('map', { zoom:12, viewMode:'3D', resizeEnable:true, rotateEnable:true, pitchEnable:true });
      map.on('complete', function(){ mapReady=true; });
      setTimeout(function(){ if(!mapReady) showFail(); }, 8000);
      try{ new AMap.AutoComplete({ input:'from' }); new AMap.AutoComplete({ input:'to' }); }catch(e){}
      try{ new AMap.CitySearch().getLocalCity(function(s,r){ if(s==='complete'&&r.city) myCity=r.city; }); }catch(e){}
      locateMe(true);
    }catch(e){ showFail(); }
  }

  function reverseGeocode(lnglat, cb){
    try{
      if(!geocoder) geocoder = new AMap.Geocoder({});
      geocoder.getAddress(lnglat, function(status,result){
        cb((status==='complete' && result.regeocode) ? result.regeocode.formattedAddress : '');
      });
    }catch(e){ cb(''); }
  }
  function locateMe(silent){
    try{
      var geo = new AMap.Geolocation({ enableHighAccuracy:true, timeout:8000 });
      geo.getCurrentPosition(function(status,result){
        if(status==='complete' && result.position){
          myPos = result.position; map.setCenter(myPos);
          var ac = result.addressComponent;
          if(ac && typeof ac.city==='string' && ac.city) myCity = ac.city;
          var setFrom = function(v){ if(v && (!$('from').value || $('from').value==='我的位置')) $('from').value = v; };
          if(result.formattedAddress){ setFrom(result.formattedAddress); }
          else reverseGeocode(myPos, setFrom);
        } else if(!silent){ toast('定位失败，请手动输入起点'); }
      });
    }catch(e){ if(!silent) toast('定位不可用'); }
  }

  // 距离（米）：优先 GeometryUtil，回退 haversine
  function dist(a,b){
    try{ if(AMap.GeometryUtil && AMap.GeometryUtil.distance) return AMap.GeometryUtil.distance(a,b); }catch(e){}
    var R=6378137, rad=Math.PI/180;
    var la1=a.getLat(), ln1=a.getLng(), la2=b.getLat(), ln2=b.getLng();
    var dLa=(la2-la1)*rad, dLn=(ln2-ln1)*rad;
    var s=Math.sin(dLa/2)*Math.sin(dLa/2)+Math.cos(la1*rad)*Math.cos(la2*rad)*Math.sin(dLn/2)*Math.sin(dLn/2);
    return 2*R*Math.asin(Math.sqrt(s));
  }
  function bearing(a,b){
    var toRad=Math.PI/180, toDeg=180/Math.PI;
    var la1=a.getLat()*toRad, la2=b.getLat()*toRad, dLng=(b.getLng()-a.getLng())*toRad;
    var y=Math.sin(dLng)*Math.cos(la2);
    var x=Math.cos(la1)*Math.sin(la2)-Math.sin(la1)*Math.cos(la2)*Math.cos(dLng);
    return (Math.atan2(y,x)*toDeg+360)%360;
  }
  function fmtDist(m){ return m>=1000 ? (m/1000).toFixed(1)+' 公里' : Math.round(m)+' 米'; }
  function fmtTime(s){ var m=Math.round(s/60); return m>=60 ? Math.floor(m/60)+' 小时 '+(m%60)+' 分' : m+' 分钟'; }

  function clearOverlays(){ overlays.forEach(function(o){ try{ o.setMap(null); }catch(e){} }); overlays=[]; }
  function clearRoutes(){ routes=[]; clearOverlays(); }

  function drivingPolicies(){
    var P = AMap.DrivingPolicy || {};
    return [ (P.LEAST_TIME!==undefined?P.LEAST_TIME:0),
             (P.LEAST_DISTANCE!==undefined?P.LEAST_DISTANCE:2),
             (P.LEAST_FEE!==undefined?P.LEAST_FEE:1) ];
  }

  function plan(){
    var from=$('from').value.trim(), to=$('to').value.trim();
    if(!from){ toast('请输入起点'); return; }
    if(!to){ toast('请输入终点'); return; }
    if(!mapReady){ toast('地图尚未就绪，请稍候'); return; }
    // 收起自动补全下拉，避免遮挡路线卡片
    try{ $('from').blur(); $('to').blur(); document.querySelectorAll('.amap-sug-result').forEach(function(e){ e.style.display='none'; }); }catch(e){}
    if(nav.timer){ clearInterval(nav.timer); nav.timer=0; } nav.on=false;
    try{ map.setRotation(0); map.setPitch(0); }catch(e){}
    $('navTop').classList.add('hidden'); $('navBottom').classList.add('hidden');
    $('topbar').classList.remove('hidden'); $('voiceFab').classList.remove('hidden'); $('loc').classList.remove('hidden');
    clearRoutes(); $('planPanel').classList.add('hidden');
    toast('正在规划路线…');
    if(curMode==='bus'){ planBus(from,to); return; }
    planDriveLike(from,to,curMode);
  }

  function toNormalized(route){
    var full=[], steps=[];
    (route.steps||[]).forEach(function(st){
      var p=st.path||[];
      steps.push({instr:st.instruction||'', action:st.action||'', road:st.road||'', dist:st.distance||0, idx:full.length});
      for(var i=0;i<p.length;i++) full.push(p[i]);
    });
    if(full.length<2 || (route.distance||0) < 50) return null;
    return { distance:route.distance, time:route.time, tolls:Math.round(route.tolls||0), path:full, steps:steps };
  }

  function planDriveLike(from,to,mode){
    var collected=[], finished=false;
    function finish(){
      if(finished) return; finished=true;
      if(!collected.length){ toast('未找到路线，换个更具体的地点试试'); return; }
      var seen={}, uniq=[];
      collected.forEach(function(r){ var k=Math.round(r.distance/60)+'_'+Math.round(r.time/40);
        if(!seen[k]){ seen[k]=1; uniq.push(r); } });
      uniq.sort(function(a,b){ return a.time-b.time; });
      routes = uniq.slice(0,3);
      labelRoutes(); selIdx=0; renderRoutes();
    }
    var pts=[{keyword:from,city:myCity},{keyword:to,city:myCity}];
    setTimeout(finish, 6000);   // 安全兜底：个别算路策略无响应也能出结果
    if(mode==='drive'){
      var pols=drivingPolicies(), pending=pols.length;
      pols.forEach(function(p){
        var d; try{ d=new AMap.Driving({policy:p}); }catch(e){ try{ d=new AMap.Driving(); }catch(e2){ d=null; } }
        if(!d){ if(--pending===0) finish(); return; }
        try{
          d.search(pts, function(status,result){
            if(status==='complete' && result.routes){
              result.routes.forEach(function(r){ var n=toNormalized(r); if(n) collected.push(n); });
            }
            if(--pending===0) finish();
          });
        }catch(e){ if(--pending===0) finish(); }
      });
    } else {
      var svc = mode==='walk' ? new AMap.Walking() : new AMap.Riding();
      svc.search(pts, function(status,result){
        if(status==='complete' && result.routes){
          result.routes.forEach(function(r){ var n=toNormalized(r); if(n) collected.push(n); });
        }
        finish();
      });
    }
  }

  function planBus(from,to){
    var opt={ city:myCity }; try{ if(AMap.TransferPolicy) opt.policy=AMap.TransferPolicy.LEAST_TIME; }catch(e){}
    var svc=new AMap.Transfer(opt);
    svc.search([{keyword:from},{keyword:to}], function(status,result){
      if(status!=='complete' || !result.plans || !result.plans.length){ toast('没有公交方案'); return; }
      routes = result.plans.slice(0,3).map(function(p,i){
        return { distance:p.distance, time:p.time, path:[], steps:[], label:'方案'+(i+1), bus:true }; });
      selIdx=0; clearOverlays(); renderCards(); $('planPanel').classList.remove('hidden');
      toast('公交方案见下方卡片；导航支持驾车/步行/骑行');
    });
  }

  function labelRoutes(){
    routes.forEach(function(r,i){ r.label = i===0 ? '最快' : ('备选'+i); });
    if(routes.length>1){
      var minD=routes[0]; routes.forEach(function(r){ if(r.distance<minD.distance) minD=r; });
      if(minD!==routes[0]) minD.label='最短';
    }
  }

  function renderRoutes(){
    clearOverlays();
    routes.forEach(function(r,i){
      if(!r.path.length) return;
      var pl=new AMap.Polyline({ path:r.path,
        strokeColor: i===selIdx ? '#1976ff' : '#9fb3c8',
        strokeWeight: i===selIdx ? 8 : 6,
        strokeOpacity: i===selIdx ? 0.95 : 0.55,
        zIndex: i===selIdx ? 60 : 40, lineJoin:'round', lineCap:'round',
        showDir: i===selIdx });
      pl.on('click', (function(idx){ return function(){ selectRoute(idx); }; })(i));
      pl.setMap(map); overlays.push(pl);
    });
    addEndpoints();
    renderCards();
    $('planPanel').classList.remove('hidden');
    try{ map.setFitView(overlays, false, [90,50,240,50]); }catch(e){ try{ map.setFitView(); }catch(e2){} }
  }

  function addEndpoints(){
    var r=routes[selIdx]; if(!r || !r.path.length) return;
    var s=r.path[0], e=r.path[r.path.length-1];
    function mk(pos,txt,color){
      return new AMap.Marker({ position:pos, offset:new AMap.Pixel(-11,-22), zIndex:80,
        content:'<div style="background:'+color+';color:#fff;width:22px;height:22px;line-height:22px;'
          +'border-radius:50% 50% 50% 0;transform:rotate(-45deg);text-align:center;font-size:12px;'
          +'box-shadow:0 1px 4px rgba(0,0,0,.4)"><span style="display:inline-block;transform:rotate(45deg)">'+txt+'</span></div>' });
    }
    var sm=mk(s,'起','#1AAD6B'), em=mk(e,'终','#F5222D'); sm.setMap(map); em.setMap(map); overlays.push(sm,em);
  }

  function renderCards(){
    var box=$('routeCards'); box.innerHTML='';
    routes.forEach(function(r,i){
      var extra = (r.tolls>0) ? (' · 过路费'+r.tolls+'元') : '';
      var c=document.createElement('div'); c.className='rcard'+(i===selIdx?' on':'');
      c.innerHTML='<div class="rt">'+fmtTime(r.time)+'</div><div class="rd">'+fmtDist(r.distance)+extra+'</div><div class="rl">'+(r.label||('路线'+(i+1)))+'</div>';
      c.addEventListener('click', (function(idx){ return function(){ selectRoute(idx); }; })(i));
      box.appendChild(c);
    });
  }

  function selectRoute(i){
    selIdx=i;
    if(routes[i] && routes[i].bus){ renderCards(); }
    else { renderRoutes(); }
  }

  // ── 导航（模拟推进）──
  function actionIcon(a){ a=a||'';
    if(/掉头/.test(a)) return '⤺';
    if(/左转|向左/.test(a)) return '↰';
    if(/右转|向右/.test(a)) return '↱';
    if(/左前|靠左/.test(a)) return '↖';
    if(/右前|靠右/.test(a)) return '↗';
    if(/到达|终点/.test(a)) return '⚑';
    return '↑';
  }
  function distBetween(i,j){ var d=0; for(var k=i;k<j && k<nav.seg.length;k++) d+=nav.seg[k]; return d; }
  function updateTurnDist(si){
    var nextIdx=(si+1<nav.stepIdx.length) ? nav.stepIdx[si+1] : (nav.full.length-1);
    $('turnDist').textContent = fmtDist(distBetween(Math.floor(nav.ci),nextIdx))+'后';
  }
  function isTurn(a){ return /左转|右转|掉头|靠左|靠右|左前|右前|向左|向右|到达|终点/.test(a||''); }
  function shortInstr(si){
    var a=nav.stepAction[si]||'', road=nav.stepRoad[si]||'';
    if(/到达|终点/.test(a)) return '即将到达目的地';
    return (a||'直行') + (road ? (' 进入'+road) : '');
  }
  function announceStepUI(si){
    $('turnIco').textContent = actionIcon(nav.stepAction[si]);
    $('turnInstr').textContent = shortInstr(si);
    updateTurnDist(si);
  }
  function speakStep(si){
    var a=nav.stepAction[si]||'';
    if(!isTurn(a)) return;   // 直行段不播报，仅转向/到达时提示（贴近真实导航）
    var d=nav.stepDist[si]||0, road=nav.stepRoad[si]||'';
    if(/到达|终点/.test(a)) speakLines(['前方'+fmtDist(d)+'，到达目的地']);
    else speakLines(['前方'+fmtDist(d)+'，'+a+(road?('，进入'+road):'')]);
  }

  function startNav(){
    var r=routes[selIdx];
    if(!r){ toast('请先选择路线'); return; }
    if(r.bus){ toast('公交暂不支持模拟导航，请选驾车/步行/骑行'); return; }
    if(!r.path.length){ toast('该路线无可导航路径'); return; }

    nav.full=r.path; nav.totalDist=r.distance; nav.totalTime=r.time;
    nav.seg=[]; nav.suffix=new Array(r.path.length); nav.suffix[r.path.length-1]=0;
    for(var k=0;k<r.path.length-1;k++) nav.seg[k]=dist(r.path[k],r.path[k+1]);
    var acc=0; for(var k=r.path.length-2;k>=0;k--){ acc+=nav.seg[k]; nav.suffix[k]=acc; }
    nav.stepIdx=r.steps.map(function(s){return s.idx;});
    nav.stepInstr=r.steps.map(function(s){return s.instr;});
    nav.stepAction=r.steps.map(function(s){return s.action;});
    nav.stepRoad=r.steps.map(function(s){return s.road;});
    nav.stepDist=r.steps.map(function(s){return s.dist;});
    nav.ci=0; nav.announced=0; nav.on=true; nav.lastBrg=-999;

    $('topbar').classList.add('hidden'); $('planPanel').classList.add('hidden');
    $('voiceFab').classList.add('hidden'); $('loc').classList.add('hidden');
    $('navTop').classList.remove('hidden'); $('navBottom').classList.remove('hidden');

    clearOverlays();
    var pl=new AMap.Polyline({ path:r.path, strokeColor:'#1976ff', strokeWeight:9, strokeOpacity:0.95,
      zIndex:60, lineJoin:'round', lineCap:'round', showDir:true }); pl.setMap(map); overlays.push(pl);
    nav.carMk=new AMap.Marker({ position:r.path[0], offset:new AMap.Pixel(-9,-9), zIndex:120,
      content:'<div class="car-dot"></div>' }); nav.carMk.setMap(map); overlays.push(nav.carMk);
    map.setZoomAndCenter(16, r.path[0]);
    try{ map.setPitch(40); }catch(e){}   // 3D 俯视透视，接近真实导航观感

    sendHost('app://nav?open=1&t='+Date.now());
    $('navRemain').textContent='剩余 '+fmtDist(r.distance)+' · '+fmtTime(r.time); $('navEta').textContent='';
    announceStepUI(0);
    speakLines(['导航开始，全程'+fmtDist(r.distance)+'，预计'+fmtTime(r.time)+'，请系好安全带。']);

    // ★ 车速拟真：模拟时长与路程成正比（15~60s，点密处慢、开阔处快，符合驾驶直觉）
    var simSec=Math.min(60, Math.max(15, nav.totalDist/400));
    var tickMs=160, stepF=nav.full.length/(simSec*1000/tickMs);
    nav.timer=setInterval(function(){ navTick(stepF); }, tickMs);
  }

  function navTick(stepN){
    if(!nav.on) return;
    nav.ci=Math.min(nav.full.length-1, nav.ci+stepN);
    var i=Math.floor(nav.ci);
    var pos=nav.full[i];
    if(nav.carMk) nav.carMk.setPosition(pos);
    try{ map.setCenter(pos); }catch(e){}
    // ★ 车头朝前：地图随行进方向旋转（非正北朝上）
    if(i>0){ var brg=bearing(nav.full[i-1], nav.full[i]);
      if(Math.abs(brg-nav.lastBrg)>8){ nav.lastBrg=brg; try{ map.setRotation((360-brg)%360); }catch(e){} } }
    var rem=nav.suffix[i]||0;
    var remTime=nav.totalDist>0 ? nav.totalTime*(rem/nav.totalDist) : 0;
    $('navRemain').textContent='剩余 '+fmtDist(rem)+' · '+fmtTime(remTime);
    var eta=new Date(Date.now()+remTime*1000);
    $('navEta').textContent='预计 '+pad(eta.getHours())+':'+pad(eta.getMinutes())+' 到达';
    var si=0; for(var k=0;k<nav.stepIdx.length;k++){ if(nav.stepIdx[k]<=i) si=k; else break; }
    if(si>nav.announced){ nav.announced=si; announceStepUI(si); speakStep(si); }
    else { updateTurnDist(si); }
    if(nav.ci>=nav.full.length-1) arrive();
  }

  function arrive(){
    if(!nav.on) return;
    if(nav.timer){ clearInterval(nav.timer); nav.timer=0; }
    $('turnIco').textContent='⚑'; $('turnInstr').textContent='已到达目的地'; $('turnDist').textContent='';
    $('navRemain').textContent='已到达目的地'; $('navEta').textContent='';
    speakLines(['您已到达目的地附近，本次导航结束。']);
  }

  function exitNav(){
    if(!nav.on && !nav.timer) return;
    if(nav.timer){ clearInterval(nav.timer); nav.timer=0; }
    nav.on=false;
    try{ if('speechSynthesis' in window) speechSynthesis.cancel(); }catch(e){}
    $('navTop').classList.add('hidden'); $('navBottom').classList.add('hidden');
    $('topbar').classList.remove('hidden'); $('voiceFab').classList.remove('hidden'); $('loc').classList.remove('hidden');
    try{ map.setRotation(0); map.setPitch(0); }catch(e){}
    sendHost('app://nav?open=0&t='+Date.now());
    if(routes.length) renderRoutes();
  }

  // ── 弹层 ──
  function closeSheets(){ $('mask').classList.add('hidden'); $('voiceSheet').classList.add('hidden'); }
  function renderPacks(){
    var box=$('packList'); box.innerHTML='';
    PACKS.forEach(function(p){
      var d=document.createElement('div'); d.className='pack'+(p.id===voiceId?' on':'');
      d.innerHTML='<div><div class="pn">'+p.name+'</div><div class="pd">'+p.desc+'</div></div><div class="chk">✓</div>';
      d.addEventListener('click', function(){ selectPack(p.id); });
      box.appendChild(d);
    });
  }
  function selectPack(id){
    voiceId=id; var p=curPack();
    $('voiceName').textContent=p.name; renderPacks();
    sendHost('app://voice?id='+encodeURIComponent(id)+'&t='+Date.now());
    speakLines(['已切换到'+p.name+'，'+p.desc+'，将由我为您导航。']);
    setTimeout(closeSheets, 350);
  }

  // ── 事件绑定 ──
  var mbtns = document.querySelectorAll('.modes button');
  $('voiceName').textContent = curPack().name;
  mbtns.forEach(function(b){ b.addEventListener('click', function(){
    mbtns.forEach(function(x){ x.classList.remove('on'); }); b.classList.add('on'); curMode=b.getAttribute('data-m');
  }); });
  $('go').addEventListener('click', plan);
  $('loc').addEventListener('click', function(){ locateMe(false); });
  $('swap').addEventListener('click', function(){ var a=$('from').value; $('from').value=$('to').value; $('to').value=a; });
  $('startNav').addEventListener('click', startNav);
  $('exitNav').addEventListener('click', exitNav);
  $('voiceFab').addEventListener('click', function(){ renderPacks(); $('mask').classList.remove('hidden'); $('voiceSheet').classList.remove('hidden'); });
  $('mask').addEventListener('click', closeSheets);

  if(document.readyState==='complete') initMap();
  else window.addEventListener('load', initMap);
})();
</script>
</body>
</html>
""";
}
