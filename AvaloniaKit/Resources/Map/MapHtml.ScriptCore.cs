namespace AvaloniaKit.Resources;

// ── MapHtml 分部：核心脚本（AMap 加载 + IIFE 开头）──
//    语音包/TTS(含 Android 原生桥)/提示音/定位与兜底/地理编码/算路多策略/
//    路况着色/路线卡片；导航部分在 ScriptNavPart。IIFE 在本分部开启、在导航分部闭合
public static partial class MapHtml
{
    private const string ScriptCorePart = """
<script>
window._AMapSecurityConfig = { securityJsCode: '__AMAP_SECURITY__' };
</script>
<script src="https://webapi.amap.com/maps?v=2.0&key=__AMAP_KEY__&plugin=AMap.Driving,AMap.Walking,AMap.Transfer,AMap.Riding,AMap.AutoComplete,AMap.PlaceSearch,AMap.Geolocation,AMap.CitySearch,AMap.Geocoder"></script>
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
  function showSpin(){ $('spin').classList.add('show'); }
  function hideSpin(){ $('spin').classList.remove('show'); }
  function pad(n){ return n<10 ? '0'+n : ''+n; }

  // 是否内嵌 iframe（Browser 端）：该环境地图旋转不渲染，导航降级为正北+箭头模式
  var IS_EMBED = (function(){ try{ return window.parent!==window; }catch(e){ return true; } })();
  function sendHost(url){
    if (IS_EMBED){ try{ window.parent.postMessage('map-msg:'+url,'*'); }catch(e){} }
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
  function stopSpeech(){
    try{ if(window.NativeTts && NativeTts.stopSpeak) NativeTts.stopSpeak(); }catch(e){}
    try{ if('speechSynthesis' in window) speechSynthesis.cancel(); }catch(e){}
  }
  function speakLines(lines){
    var p = curPack();
    // ★ Android WebView 无 Web Speech API：优先走原生 TTS 桥（window.NativeTts）
    if(window.NativeTts && typeof NativeTts.speak==='function'){
      try{ NativeTts.stopSpeak(); }catch(e){}
      for(var i=0;i<lines.length;i++){ try{ NativeTts.speak(lines[i], p.rate, p.pitch); }catch(e){} }
      return;
    }
    if(!('speechSynthesis' in window)){ return; }
    speechSynthesis.cancel();
    for(var j=0;j<lines.length;j++){
      var u = new SpeechSynthesisUtterance(lines[j]);
      u.lang='zh-CN'; u.rate=p.rate; u.pitch=p.pitch;
      var v = pickVoice(p.sex); if(v) u.voice=v;
      speechSynthesis.speak(u);
    }
  }
  // 高德式提示音（叮咚双音）：WebAudio，开始导航/到达时播放
  var audioCtx=null;
  function chime(){
    try{
      audioCtx = audioCtx || new (window.AudioContext||window.webkitAudioContext)();
      if(audioCtx.state==='suspended') audioCtx.resume();
      var t=audioCtx.currentTime;
      [[880,0],[660,0.18]].forEach(function(nf){
        var o=audioCtx.createOscillator(), g=audioCtx.createGain();
        o.type='sine'; o.frequency.value=nf[0];
        g.gain.setValueAtTime(0.001, t+nf[1]);
        g.gain.exponentialRampToValueAtTime(0.22, t+nf[1]+0.02);
        g.gain.exponentialRampToValueAtTime(0.001, t+nf[1]+0.28);
        o.connect(g); g.connect(audioCtx.destination);
        o.start(t+nf[1]); o.stop(t+nf[1]+0.3);
      });
    }catch(e){}
  }
  // 结束提示音（下行双音）：主动退出导航时播放，音色区别于开始/到达
  function endChime(){
    try{
      audioCtx = audioCtx || new (window.AudioContext||window.webkitAudioContext)();
      if(audioCtx.state==='suspended') audioCtx.resume();
      var t=audioCtx.currentTime;
      [[620,0],[430,0.2]].forEach(function(nf){
        var o=audioCtx.createOscillator(), g=audioCtx.createGain();
        o.type='sine'; o.frequency.value=nf[0];
        g.gain.setValueAtTime(0.001, t+nf[1]);
        g.gain.exponentialRampToValueAtTime(0.2, t+nf[1]+0.02);
        g.gain.exponentialRampToValueAtTime(0.001, t+nf[1]+0.32);
        o.connect(g); g.connect(audioCtx.destination);
        o.start(t+nf[1]); o.stop(t+nf[1]+0.34);
      });
    }catch(e){}
  }

  // ── 位置箭头图标（大三角，随行进方向 setAngle 旋转）──
  var ARROW_SVG = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="46" height="46" viewBox="0 0 46 46">'
    + '<circle cx="23" cy="23" r="20" fill="#1677ff" fill-opacity="0.16"/>'
    + '<circle cx="23" cy="23" r="12" fill="#ffffff"/>'
    + '<path d="M23 7 L34 37 L23 30 L12 37 Z" fill="#1677ff" stroke="#ffffff" stroke-width="1.8" stroke-linejoin="round"/>'
    + '</svg>');

  // ── 地图 ──
  var map=null, mapReady=false, curMode='drive', myCity='北京', myPos=null, geocoder=null;
  var routes=[], overlays=[], selIdx=0;
  var fromLL=null, toLL=null;   // 起终点已定位坐标（有则直接按坐标算路，避免长地址关键词检索失败）
  var nav={ on:false, full:[], seg:[], suffix:[], ci:0, stepIdx:[], stepInstr:[], stepAction:[],
            stepRoad:[], stepDist:[], announced:-1, totalDist:0, totalTime:0, carMk:null, destLL:null,
            watchGeo:null, watchId:null, goodFix:false, lastFixPos:null, lastFixTime:0, speedKmh:-1,
            offCnt:0, rerouting:false, arrived:false, lastProc:0, staleTimer:0, lastBrg:-999 };

  function showFail(){ $('fail').classList.remove('hidden'); }

  function initMap(){
    if (typeof AMap === 'undefined'){ showFail(); return; }
    try{
      map = new AMap.Map('map', { zoom:12, viewMode:'2D', resizeEnable:true });
      map.on('complete', function(){ mapReady=true; });
      setTimeout(function(){ if(!mapReady) showFail(); }, 8000);
      try{ new AMap.AutoComplete({ input:'from' }); new AMap.AutoComplete({ input:'to' }); }catch(e){}
      try{ new AMap.CitySearch().getLocalCity(function(s,r){ if(s==='complete'&&r.city) myCity=r.city; }); }catch(e){}
      locate(function(){});   // 启动静默定位，居中地图
    }catch(e){ showFail(); }
  }

  function getGeocoder(){ if(!geocoder) geocoder = new AMap.Geocoder({ city: myCity }); return geocoder; }
  function reverseGeocode(lnglat, cb){
    try{
      getGeocoder().getAddress(lnglat, function(status,result){
        cb((status==='complete' && result.regeocode) ? result.regeocode.formattedAddress : '');
      });
    }catch(e){ cb(''); }
  }
  // 地点文本 → 坐标（POI 名/地址皆可）：PlaceSearch 优先，Geocoder 兜底，带超时防卡死
  function geocodeOne(text, cb){
    var done=false, to=setTimeout(function(){ if(!done){ done=true; cb(null); } }, 6000);
    function fin(ll){ if(done) return; done=true; clearTimeout(to); cb(ll); }
    function byGeocoder(){
      try{ getGeocoder().getLocation(text, function(s,r){
        fin((s==='complete' && r.geocodes && r.geocodes.length) ? r.geocodes[0].location : null);
      }); }catch(e){ fin(null); }
    }
    try{
      var ps=new AMap.PlaceSearch({ city: myCity, pageSize:1 });
      ps.search(text, function(status,result){
        if(status==='complete' && result.poiList && result.poiList.pois && result.poiList.pois.length)
          fin(result.poiList.pois[0].location);
        else byGeocoder();
      });
    }catch(e){ byGeocoder(); }
  }
  // 解析一个端点为坐标：已定位的直接用坐标，否则按文本地理编码
  function resolvePoint(text, ll, cb){ if(ll){ cb(ll); return; } if(!text){ cb(null); return; } geocodeOne(text, cb); }

  // 统一定位：H5 精确定位优先，失败回退 IP 城市级定位（桌面无定位硬件时兜底）
  // cb(addr, coarse)：addr=null 彻底失败；coarse=true 表示 IP 级精度（城市中心）
  function locate(cb){
    function ipFallback(){
      try{
        new AMap.CitySearch().getLocalCity(function(s,r){
          if(s==='complete' && r.bounds){
            var c=r.bounds.getCenter();
            myPos=c; map.setCenter(c);
            if(typeof r.city==='string' && r.city) myCity=r.city;
            reverseGeocode(c, function(a){ cb(a || ((r.city||'')+'中心'), true); });
          } else cb(null, false);
        });
      }catch(e){ cb(null, false); }
    }
    try{
      var geo = new AMap.Geolocation({ enableHighAccuracy:true, timeout:8000 });
      geo.getCurrentPosition(function(status,result){
        if(status==='complete' && result.position){
          myPos = result.position; map.setCenter(myPos);
          var ac = result.addressComponent;
          if(ac && typeof ac.city==='string' && ac.city) myCity = ac.city;
          var coarse = (result.location_type==='ip') || (result.accuracy||0) > 1000;
          if(result.formattedAddress){ cb(result.formattedAddress, coarse); }
          else reverseGeocode(myPos, function(a){ cb(a || '', coarse); });
        } else ipFallback();
      });
    }catch(e){ ipFallback(); }
  }

  // 输入框「定位」按钮：把当前位置填入指定输入框
  function locateInto(which){
    var input=$(which); var old=input.value;
    input.value='定位中…'; input.disabled=true;
    locate(function(addr, coarse){
      input.disabled=false;
      if(addr){ input.value=addr; if(which==='from') fromLL=myPos; else toLL=myPos;
        toast(coarse ? '已按网络定位到城市（精度有限，可手动修正）' : '已定位到当前位置'); }
      else { input.value=old; toast('定位失败，请检查系统定位服务与权限，或手动输入'); }
    });
  }

  // 右下角「回到我的位置」
  function recenter(){
    if(myPos){ map.setZoomAndCenter(15, myPos); }
    else locate(function(addr){ if(!addr) toast('定位失败，请检查定位权限'); });
  }

  function dist(a,b){
    try{ if(AMap.GeometryUtil && AMap.GeometryUtil.distance) return AMap.GeometryUtil.distance(a,b); }catch(e){}
    var R=6378137, rad=Math.PI/180;
    var la1=a.getLat(), la2=b.getLat();
    var dLa=(la2-la1)*rad, dLn=(b.getLng()-a.getLng())*rad;
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
  // 环形角度差（0~180）
  function angDiff(a,b){ var d=Math.abs(a-b)%360; return d>180 ? 360-d : d; }
  // 沿方位角前移 meters 米的坐标（heading-up 时作视野中心，让车居屏幕中下、多看前方）
  function pointAhead(p, brg, meters){
    var R=6378137, rad=Math.PI/180;
    var la=p.getLat()*rad, ln=p.getLng()*rad, b=brg*rad, dr=meters/R;
    var la2=Math.asin(Math.sin(la)*Math.cos(dr)+Math.cos(la)*Math.sin(dr)*Math.cos(b));
    var ln2=ln+Math.atan2(Math.sin(b)*Math.sin(dr)*Math.cos(la), Math.cos(dr)-Math.sin(la)*Math.sin(la2));
    return new AMap.LngLat(ln2/rad, la2/rad);
  }
  function fmtDist(m){ return m>=1000 ? (m/1000).toFixed(1)+'公里' : Math.round(m)+'米'; }
  function fmtTime(s){ var m=Math.max(1,Math.round(s/60)); return m>=60 ? Math.floor(m/60)+'小时'+(m%60)+'分' : m+'分钟'; }

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
    try{ $('from').blur(); $('to').blur(); document.querySelectorAll('.amap-sug-result').forEach(function(e){ e.style.display='none'; }); }catch(e){}
    stopGpsWatch(); nav.on=false; nav.arrived=false;
    try{ map.setRotation(0); }catch(e){}
    $('speedBall').classList.add('hidden'); $('navRail').classList.add('hidden');
    $('navTop').classList.add('hidden'); $('navBottom').classList.add('hidden');
    $('topbar').classList.remove('hidden'); $('voiceFab').classList.remove('hidden'); $('loc').classList.remove('hidden');
    clearRoutes(); $('planPanel').classList.add('hidden');
    showSpin();
    var mode=curMode;
    setTimeout(function(){ if($('spin').classList.contains('show')){ hideSpin(); toast('规划超时，请重试或换个地点'); } }, 12000);
    // ★ 先把起终点解析为坐标（已定位端直接用坐标，避免长地址关键词检索失败），再按坐标算路
    resolvePoint(from, fromLL, function(o){
      if(!o){ hideSpin(); toast('起点无法定位，请换个更具体的地点'); return; }
      resolvePoint(to, toLL, function(dst){
        if(!dst){ hideSpin(); toast('终点无法定位，请换个更具体的地点'); return; }
        if(mode==='bus') planBusLL(o, dst); else planDriveLikeLL(o, dst, mode);
      });
    });
  }

  function toNormalized(route){
    var full=[], steps=[], tmcs=[], roadDist={};
    (route.steps||[]).forEach(function(st){
      var p=st.path||[];
      steps.push({instr:st.instruction||'', action:st.action||'', road:st.road||'', dist:st.distance||0, idx:full.length});
      if(st.road) roadDist[st.road]=(roadDist[st.road]||0)+(st.distance||0);
      // TMC 路况分段（extensions:'all' 时提供），相邻同状态合并降低绘制开销
      var tp=st.tmcsPaths || st.tmcs_paths || [];
      for(var t=0;t<tp.length;t++){
        var seg=tp[t]; if(!seg || !seg.path || seg.path.length<2) continue;
        var ss=seg.status||'';
        if(tmcs.length && tmcs[tmcs.length-1].status===ss)
          tmcs[tmcs.length-1].path=tmcs[tmcs.length-1].path.concat(seg.path);
        else tmcs.push({ path:seg.path.slice(), status:ss });
      }
      for(var i=0;i<p.length;i++) full.push(p[i]);
    });
    if(full.length<2 || (route.distance||0) < 50) return null;
    // 途经主要道路（按里程取前两条：自然包含高速/干道与小路信息）
    var via=Object.keys(roadDist).sort(function(a,b){ return roadDist[b]-roadDist[a]; }).slice(0,2).join(' · ');
    return { distance:route.distance, time:route.time, tolls:Math.round(route.tolls||0), path:full, steps:steps, tmcs:tmcs, via:via };
  }

  function planDriveLikeLL(origin,dest,mode){
    var collected=[], finished=false;
    function finish(){
      if(finished) return; finished=true; hideSpin();
      if(!collected.length){ toast('未找到路线，换个更具体的地点试试'); return; }
      var seen={}, uniq=[];
      collected.forEach(function(r){ var k=Math.round(r.distance/60)+'_'+Math.round(r.time/40);
        if(!seen[k]){ seen[k]=1; uniq.push(r); } });
      uniq.sort(function(a,b){ return a.time-b.time; });
      routes = uniq.slice(0,3);
      labelRoutes(); selIdx=0; renderRoutes();
    }
    setTimeout(finish, 6000);
    if(mode==='drive'){
      var pols=drivingPolicies(), pending=pols.length;
      pols.forEach(function(p){
        var d; try{ d=new AMap.Driving({policy:p, extensions:'all'}); }catch(e){ try{ d=new AMap.Driving(); }catch(e2){ d=null; } }
        if(!d){ if(--pending===0) finish(); return; }
        try{
          d.search(origin, dest, function(status,result){
            if(status==='complete' && result.routes){
              result.routes.forEach(function(r){ var n=toNormalized(r); if(n) collected.push(n); });
            }
            if(--pending===0) finish();
          });
        }catch(e){ if(--pending===0) finish(); }
      });
    } else {
      var svc = mode==='walk' ? new AMap.Walking() : new AMap.Riding();
      svc.search(origin, dest, function(status,result){
        if(status==='complete' && result.routes){
          result.routes.forEach(function(r){ var n=toNormalized(r); if(n) collected.push(n); });
        }
        finish();
      });
    }
  }

  function planBusLL(origin,dest){
    var opt={ city:myCity }; try{ if(AMap.TransferPolicy) opt.policy=AMap.TransferPolicy.LEAST_TIME; }catch(e){}
    var svc=new AMap.Transfer(opt);
    svc.search(origin, dest, function(status,result){
      hideSpin();
      if(status!=='complete' || !result.plans || !result.plans.length){ toast('没有公交方案，换个地点试试'); return; }
      routes = result.plans.slice(0,3).map(function(p,i){
        return { distance:p.distance, time:p.time, path:[], steps:[], label:'方案'+(i+1), bus:true }; });
      selIdx=0; clearOverlays(); renderCards(); $('planPanel').classList.remove('hidden');
      toast('公交方案见下方卡片；导航支持驾车/步行/骑行');
    });
  }

  function labelRoutes(){
    routes.forEach(function(r,i){ r.label = i===0 ? '时间最短' : ('备选'+i); });
    if(routes.length>1){
      var minD=routes[0]; routes.forEach(function(r){ if(r.distance<minD.distance) minD=r; });
      if(minD!==routes[0]) minD.label='距离最短';
    }
  }

  // TMC 路况配色（畅通绿/缓行黄/拥堵红/严重深红；未知走主线蓝）
  function tmcColor(s){
    if(/严重/.test(s||'')) return '#a10d0d';
    if(/拥堵/.test(s||'')) return '#f5432c';
    if(/缓/.test(s||''))   return '#ffb400';
    if(/畅/.test(s||''))   return '#34c759';
    return '';
  }
  // 按路况着色绘制路线：蓝色主线打底，TMC 分段覆盖红绿黄（与高德一致）
  function drawColoredRoute(r, zBase){
    var base=new AMap.Polyline({ path:r.path, strokeColor:'#1677ff', strokeWeight:9, strokeOpacity:.95,
      zIndex:zBase, lineJoin:'round', lineCap:'round', showDir:true, isOutline:true, outlineColor:'#fff', borderWeight:2 });
    base.setMap(map); overlays.push(base);
    (r.tmcs||[]).forEach(function(t){
      var c=tmcColor(t.status); if(!c) return;
      var pl=new AMap.Polyline({ path:t.path, strokeColor:c, strokeWeight:9, strokeOpacity:.95,
        zIndex:zBase+1, lineJoin:'round', lineCap:'round', showDir:true });
      pl.setMap(map); overlays.push(pl);
    });
  }
  // 右侧垂直里程光柱：按 TMC 段距离占比着色（底=起点 顶=终点）
  function buildRail(r){
    var el=$('railTrack');
    var segs=r.tmcs||[];
    if(!segs.length){ el.style.background='#34c759'; return; }
    var ds=segs.map(function(t){ var d=0; for(var i=0;i<t.path.length-1;i++) d+=dist(t.path[i],t.path[i+1]); return d; });
    var sum=ds.reduce(function(a,b){ return a+b; }, 0);
    if(sum<=0){ el.style.background='#34c759'; return; }
    var g='linear-gradient(to top', acc=0;
    for(var i2=0;i2<segs.length;i2++){
      var c=tmcColor(segs[i2].status)||'#34c759';
      var from=acc/sum*100; acc+=ds[i2]; var to=acc/sum*100;
      g+=', '+c+' '+from.toFixed(1)+'%, '+c+' '+to.toFixed(1)+'%';
    }
    el.style.background=g+')';
  }

  function renderRoutes(){
    clearOverlays();
    routes.forEach(function(r,i){
      if(!r.path.length || i===selIdx) return;
      var pl=new AMap.Polyline({ path:r.path, strokeColor:'#aab6c6', strokeWeight:6, strokeOpacity:0.5,
        zIndex:40, lineJoin:'round', lineCap:'round' });
      pl.on('click', (function(idx){ return function(){ selectRoute(idx); }; })(i));
      pl.setMap(map); overlays.push(pl);
    });
    if(routes[selIdx] && routes[selIdx].path.length) drawColoredRoute(routes[selIdx], 60);
    addEndpoints();
    renderCards();
    $('planPanel').classList.remove('hidden'); $('planPanel').classList.remove('collapsed');
    try{ map.setFitView(overlays, false, [96,50,250,50]); }catch(e){ try{ map.setFitView(); }catch(e2){} }
  }

  function addEndpoints(){
    var r=routes[selIdx]; if(!r || !r.path.length) return;
    var s=r.path[0], e=r.path[r.path.length-1];
    function mk(pos,txt,color){
      return new AMap.Marker({ position:pos, offset:new AMap.Pixel(-13,-30), zIndex:90,
        content:'<div style="width:26px;height:26px;line-height:26px;background:'+color+';color:#fff;'
          +'border-radius:50% 50% 50% 0;transform:rotate(-45deg);text-align:center;font-size:13px;font-weight:600;'
          +'box-shadow:0 2px 6px rgba(0,0,0,.35)"><span style="display:inline-block;transform:rotate(45deg)">'+txt+'</span></div>' });
    }
    var sm=mk(s,'起','#12b36a'), em=mk(e,'终','#f5432c'); sm.setMap(map); em.setMap(map); overlays.push(sm,em);
  }

  function renderCards(){
    var box=$('routeCards'); box.innerHTML='';
    routes.forEach(function(r,i){
      var extra = (r.tolls>0) ? (' · 过路费'+r.tolls+'元') : (r.bus ? '' : ' · 无过路费');
      var tag = (i===0 && !r.bus) ? '<span class="tag">推荐</span>' : '';
      var via=(r.via && !r.bus) ? ('<div class="rv">途经 '+r.via+'</div>') : '';
      var c=document.createElement('div'); c.className='rcard'+(i===selIdx?' on':'');
      c.innerHTML=tag+'<div class="rt">'+fmtTime(r.time)+'</div><div class="rd">'+fmtDist(r.distance)+extra+'</div>'+via+'<div class="rl">'+(r.label||('路线'+(i+1)))+'</div>';
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
    if(/到达|终点/.test(a)) return '🏁';
    return '↑';
  }
  function distBetween(i,j){ var d=0; for(var k=i;k<j && k<nav.seg.length;k++) d+=nav.seg[k]; return d; }
  function updateTurnDist(si){
    var nextIdx=(si+1<nav.stepIdx.length) ? nav.stepIdx[si+1] : (nav.full.length-1);
    var d=distBetween(Math.floor(nav.ci),nextIdx);
    $('turnDist').innerHTML = '<b>'+(d>=1000?(d/1000).toFixed(1):Math.round(d))+'</b>'+(d>=1000?'公里':'米')+'后';
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
    if(!isTurn(a)) return;
    var d=nav.stepDist[si]||0, road=nav.stepRoad[si]||'';
    if(/到达|终点/.test(a)) speakLines(['前方'+fmtDist(d)+'，到达目的地']);
    else speakLines(['前方'+fmtDist(d)+'，'+a+(road?('，进入'+road):'')]);
  }

""";
}
