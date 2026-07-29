namespace AvaloniaKit.Resources;

// ══════════════════════════════════════════════════════════════════════════════
//  MapHtml — 地图页面（一份 HTML，四端复用；仿高德地图，实用导航工具）
//  · 地图与路线：高德 JS API v2.0（AMap.Driving/Walking/Riding/Transfer）
//  · 查路线：多策略并行算路去重 → 多条备选路线，点线/卡片切换选中
//  · 起点/终点输入框内置「定位」按钮：一键填入当前位置；右下角「回到我的位置」悬浮键
//  · 导航：真实 GPS 跟随（watchPosition 吸附路线/偏航自动重算/到达判定/实测车速球）；
//    无有效定位信号（如桌面无定位硬件）自动回退模拟巡航并明确语音+文字提示
//  · 位置点为大三角方向箭头（随行进方向旋转）+ 转向卡 + 剩余里程/ETA + 进度条 +
//    按所选语音包 TTS 逐路口精简播报；「退出」回路线选择
//  · 定位：H5 精确定位优先，失败回退 IP 城市级定位（提示区分精度）
//  · 语音包：SpeechSynthesis(TTS) 用音色/语速/音调模拟 5 个语音包
//  · JS→宿主消息：app://voice?id=（切语音包持久化）、app://nav?open=1/0（导航态）
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
  :root { --ac:#1677ff; --ink:#1a1f2b; --sub:#8a92a0; --line:#eef0f2; --bg:#f4f6f8; }
  * { margin:0; padding:0; box-sizing:border-box; -webkit-tap-highlight-color:transparent; }
  html,body { height:100%; font-family:-apple-system,"PingFang SC","Microsoft YaHei",sans-serif; color:#1a1f2b; }
  #map { position:absolute; inset:0; background:#e6e8eb; }
  button { cursor:pointer; font-family:inherit; }

  /* ── 顶部搜索卡 ── */
  .topbar { position:absolute; left:10px; right:10px; top:10px; z-index:20;
    background:#fff; border-radius:16px; padding:12px 12px 12px; box-shadow:0 6px 22px rgba(0,0,0,.16); }
  .route-row { display:flex; align-items:center; }
  .io-wrap { flex:1; min-width:0; }
  .io { display:flex; align-items:center; height:44px; }
  .io + .io { border-top:1px solid var(--line); }
  .dot { width:9px; height:9px; border-radius:50%; margin-right:10px; flex:none; }
  .dot.start { background:#12b36a; box-shadow:0 0 0 3px rgba(18,179,106,.15); }
  .dot.end { background:#f5432c; box-shadow:0 0 0 3px rgba(245,67,44,.15); }
  .io input { flex:1; min-width:0; border:none; outline:none; font-size:15px; background:transparent; color:#1a1f2b; }
  .io input::placeholder { color:#aab1bd; }
  .io-loc { flex:none; width:34px; height:34px; border:none; background:transparent;
    display:flex; align-items:center; justify-content:center; border-radius:9px; }
  .io-loc:active { background:#eaf2ff; }
  #swap { flex:none; width:36px; height:36px; margin-left:8px; border:none; background:var(--bg);
    border-radius:50%; color:#606a7b; font-size:17px; display:flex; align-items:center; justify-content:center; }
  #swap:active { background:#e6eaf0; }
  .modes { display:flex; gap:6px; margin-top:12px; }
  .modes button { flex:1; height:34px; border:none; border-radius:9px; background:var(--bg); color:#5a6270; font-size:14px; }
  .modes button.on { background:#eaf2ff; color:var(--ac); font-weight:700; }
  #go { width:100%; height:42px; margin-top:11px; border:none; border-radius:11px;
    background:var(--ac); color:#fff; font-size:15px; font-weight:700; box-shadow:0 4px 12px rgba(22,119,255,.32); }
  #go:active { background:#0f63e6; }

  /* ── 悬浮键 ── */
  .voice-fab { position:absolute; left:12px; bottom:160px; z-index:21; height:40px; padding:0 15px;
    border:none; border-radius:20px; background:#fff; box-shadow:0 4px 14px rgba(0,0,0,.18);
    color:var(--ac); font-size:14px; font-weight:600; display:flex; align-items:center; gap:5px; }
  .loc-fab { position:absolute; right:12px; bottom:160px; z-index:21; width:44px; height:44px; border:none;
    border-radius:50%; background:#fff; box-shadow:0 4px 14px rgba(0,0,0,.2);
    display:flex; align-items:center; justify-content:center; }
  .loc-fab:active { background:#f0f4fa; }

  /* ── 多路线卡片 + 开始导航 ── */
  .plan-panel { position:absolute; left:0; right:0; bottom:0; z-index:22; background:#fff;
    border-radius:18px 18px 0 0; box-shadow:0 -4px 20px rgba(0,0,0,.15); padding:14px 12px 16px; }
  .plan-panel::before { content:''; position:absolute; top:7px; left:50%; transform:translateX(-50%);
    width:38px; height:4px; border-radius:2px; background:#e2e6ec; }
  .rcards { display:flex; gap:9px; overflow-x:auto; padding:6px 0 2px; }
  .rcard { position:relative; flex:0 0 auto; min-width:126px; padding:11px 13px; border-radius:13px;
    background:var(--bg); border:1.5px solid transparent; transition:all .15s; }
  .rcard.on { background:#eaf2ff; border-color:var(--ac); box-shadow:0 3px 10px rgba(22,119,255,.22); }
  .rcard .rt { font-size:19px; font-weight:800; color:var(--ac); letter-spacing:-.2px; }
  .rcard .rd { font-size:12px; color:#6b7280; margin-top:4px; white-space:nowrap; }
  .rcard .rl { font-size:11px; color:#9aa1ad; margin-top:5px; }
  .rcard.on .rl { color:var(--ac); font-weight:600; }
  .rcard .tag { position:absolute; top:-1px; right:-1px; background:var(--ac); color:#fff;
    font-size:10px; font-weight:600; padding:2px 7px; border-radius:0 12px 0 10px; }
  .start-nav { width:100%; height:46px; margin-top:12px; border:none; border-radius:12px;
    background:var(--ac); color:#fff; font-size:16px; font-weight:700; box-shadow:0 4px 12px rgba(22,119,255,.32);
    display:flex; align-items:center; justify-content:center; gap:7px; }
  .start-nav:active { background:#0f63e6; }

  /* ── 导航视图 ── */
  .nav-top { position:absolute; left:10px; right:10px; top:10px; z-index:25;
    background:linear-gradient(135deg,#1f7bff,#0e63e8); color:#fff; border-radius:16px;
    padding:13px 16px; display:flex; align-items:center; gap:15px; box-shadow:0 6px 20px rgba(14,99,232,.4); }
  .nav-top .turn-ico { font-size:40px; line-height:1; width:44px; text-align:center; flex:none; }
  .nav-top .turn-dist { font-size:14px; opacity:.92; }
  .nav-top .turn-dist b { font-size:22px; font-weight:800; margin-right:2px; }
  .nav-top .turn-instr { font-size:18px; font-weight:700; margin-top:3px;
    white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .nav-bottom { position:absolute; left:10px; right:10px; bottom:12px; z-index:25; background:#fff;
    border-radius:16px; padding:13px 14px 12px; display:flex; align-items:center;
    box-shadow:0 4px 18px rgba(0,0,0,.18); overflow:hidden; }
  .nav-prog { position:absolute; left:0; top:0; height:3px; width:100%; background:var(--line); }
  .nav-prog i { display:block; height:100%; width:0; background:var(--ac); transition:width .2s linear; }
  .nav-info { flex:1; }
  .nav-info b { font-size:19px; color:var(--ac); font-weight:800; }
  .nav-info span { display:block; font-size:12px; color:var(--sub); margin-top:3px; }
  .exit-nav { border:none; background:#fff; color:#f5432c; border:1.5px solid #f5432c; border-radius:11px;
    height:40px; padding:0 18px; font-size:15px; font-weight:600; }
  .exit-nav:active { background:#fff0ee; }
  .speed-ball { position:absolute; left:12px; bottom:96px; z-index:25; width:66px; height:66px;
    border-radius:50%; background:#fff; border:3px solid var(--ac); box-shadow:0 4px 14px rgba(0,0,0,.22);
    display:flex; flex-direction:column; align-items:center; justify-content:center; }
  .speed-ball b { font-size:22px; color:#1a1f2b; line-height:1; }
  .speed-ball span { font-size:10px; color:#8a92a0; margin-top:3px; }

  /* ── 底部弹层（语音包） ── */
  .mask { position:absolute; inset:0; z-index:30; background:rgba(0,0,0,.4); }
  .sheet { position:absolute; left:0; right:0; bottom:0; z-index:31; background:#fff;
    border-radius:18px 18px 0 0; padding:8px 16px 18px; max-height:72%; overflow:auto; }
  .sheet::before { content:''; display:block; width:38px; height:4px; border-radius:2px;
    background:#e2e6ec; margin:8px auto 12px; }
  .sheet h3 { font-size:16px; color:#1a1f2b; margin-bottom:8px; }
  .pack { display:flex; align-items:center; justify-content:space-between; padding:14px 12px; border-radius:12px; }
  .pack.on { background:#eaf2ff; }
  .pack .pn { font-size:15px; color:#1a1f2b; font-weight:500; }
  .pack .pd { font-size:12px; color:#9aa1ad; margin-top:2px; }
  .pack .chk { color:var(--ac); font-weight:800; font-size:17px; visibility:hidden; }
  .pack.on .chk { visibility:visible; }

  /* ── 提示 / 加载 / 失败 ── */
  .toast { position:absolute; left:50%; top:42%; transform:translate(-50%,-50%); z-index:50;
    background:rgba(30,34,42,.92); color:#fff; padding:11px 18px; border-radius:12px; font-size:14px;
    opacity:0; transition:opacity .22s; pointer-events:none; max-width:78%; text-align:center; line-height:1.5;
    box-shadow:0 6px 20px rgba(0,0,0,.28); }
  .toast.show { opacity:1; }
  .spin { position:absolute; left:50%; top:42%; transform:translate(-50%,-50%); z-index:46;
    background:rgba(30,34,42,.9); color:#fff; padding:16px 22px; border-radius:14px; font-size:13px;
    display:none; flex-direction:column; align-items:center; gap:10px; }
  .spin.show { display:flex; }
  .spin-c { width:28px; height:28px; border:3px solid rgba(255,255,255,.28); border-top-color:#fff;
    border-radius:50%; animation:spin .8s linear infinite; }
  @keyframes spin { to { transform:rotate(360deg); } }

  .fail { position:absolute; inset:0; z-index:40; background:#f5f6f8; display:flex; flex-direction:column;
    align-items:center; justify-content:center; gap:14px; }
  .fail .em { font-size:48px; } .fail .tx { color:#6b7280; font-size:14px; text-align:center; padding:0 34px; line-height:1.8; }
  .hidden { display:none !important; }
</style>
</head>
<body>
<div id="map"></div>

<div class="topbar" id="topbar">
  <div class="route-row">
    <div class="io-wrap">
      <div class="io">
        <span class="dot start"></span>
        <input id="from" placeholder="选择起点">
        <button class="io-loc" data-t="from" title="定位到当前位置">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none">
            <circle cx="12" cy="12" r="3.1" fill="#1677ff"/>
            <circle cx="12" cy="12" r="7" stroke="#1677ff" stroke-width="1.6"/>
            <path d="M12 1.4V4.2M12 19.8V22.6M1.4 12H4.2M19.8 12H22.6" stroke="#1677ff" stroke-width="1.6" stroke-linecap="round"/>
          </svg>
        </button>
      </div>
      <div class="io">
        <span class="dot end"></span>
        <input id="to" placeholder="输入终点">
        <button class="io-loc" data-t="to" title="定位到当前位置">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none">
            <circle cx="12" cy="12" r="3.1" fill="#1677ff"/>
            <circle cx="12" cy="12" r="7" stroke="#1677ff" stroke-width="1.6"/>
            <path d="M12 1.4V4.2M12 19.8V22.6M1.4 12H4.2M19.8 12H22.6" stroke="#1677ff" stroke-width="1.6" stroke-linecap="round"/>
          </svg>
        </button>
      </div>
    </div>
    <button id="swap" title="交换起终点">⇅</button>
  </div>
  <div class="modes">
    <button data-m="drive" class="on">驾车</button>
    <button data-m="walk">步行</button>
    <button data-m="bus">公交</button>
    <button data-m="ride">骑行</button>
  </div>
  <button id="go">查路线</button>
</div>

<button class="voice-fab" id="voiceFab">🎙 <span id="voiceName">语音包</span></button>
<button class="loc-fab" id="loc" title="回到我的位置">
  <svg width="22" height="22" viewBox="0 0 24 24" fill="none">
    <circle cx="12" cy="12" r="3.3" fill="#1677ff"/>
    <circle cx="12" cy="12" r="7.6" stroke="#1677ff" stroke-width="1.8"/>
    <path d="M12 1V4.3M12 19.7V23M1 12H4.3M19.7 12H23" stroke="#1677ff" stroke-width="1.8" stroke-linecap="round"/>
  </svg>
</button>

<div class="plan-panel hidden" id="planPanel">
  <div class="rcards" id="routeCards"></div>
  <button class="start-nav" id="startNav">🧭 开始导航</button>
</div>

<div class="nav-top hidden" id="navTop">
  <div class="turn-ico" id="turnIco">↑</div>
  <div class="turn-main" style="min-width:0;flex:1">
    <div class="turn-dist" id="turnDist"></div>
    <div class="turn-instr" id="turnInstr">开始导航</div>
  </div>
</div>
<div class="nav-bottom hidden" id="navBottom">
  <div class="nav-prog"><i id="navProgFill"></i></div>
  <div class="nav-info"><b id="navRemain">--</b><span id="navEta"></span></div>
  <button class="exit-nav" id="exitNav">退出</button>
</div>
<div class="speed-ball hidden" id="speedBall"><b id="speedVal">0</b><span>km/h</span></div>

<div class="mask hidden" id="mask"></div>
<div class="sheet hidden" id="voiceSheet"><h3>选择语音包</h3><div id="packList"></div></div>

<div class="toast" id="toast"></div>
<div class="spin" id="spin"><div class="spin-c"></div><span>正在规划路线…</span></div>
<div class="fail hidden" id="fail">
  <div class="em">🛰️</div>
  <div class="tx">地图加载失败：请检查网络，或确认高德 Key 已正确配置（Web 端 JS API + 安全密钥，且域名白名单留空）。</div>
</div>

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
  var nav={ on:false, mode:'gps', timer:0, full:[], seg:[], suffix:[], ci:0, stepIdx:[], stepInstr:[], stepAction:[],
            stepRoad:[], stepDist:[], announced:-1, totalDist:0, totalTime:0, carMk:null, destLL:null,
            watchGeo:null, watchId:null, goodFix:false, lastFixPos:null, lastFixTime:0, speedKmh:-1,
            offCnt:0, rerouting:false, arrived:false, lastProc:0, staleTimer:0 };

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
    if(nav.timer){ clearInterval(nav.timer); nav.timer=0; } stopGpsWatch(); nav.on=false;
    $('speedBall').classList.add('hidden');
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
    var full=[], steps=[];
    (route.steps||[]).forEach(function(st){
      var p=st.path||[];
      steps.push({instr:st.instruction||'', action:st.action||'', road:st.road||'', dist:st.distance||0, idx:full.length});
      for(var i=0;i<p.length;i++) full.push(p[i]);
    });
    if(full.length<2 || (route.distance||0) < 50) return null;
    return { distance:route.distance, time:route.time, tolls:Math.round(route.tolls||0), path:full, steps:steps };
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
        var d; try{ d=new AMap.Driving({policy:p}); }catch(e){ try{ d=new AMap.Driving(); }catch(e2){ d=null; } }
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

  function renderRoutes(){
    clearOverlays();
    routes.forEach(function(r,i){
      if(!r.path.length) return;
      var pl=new AMap.Polyline({ path:r.path,
        strokeColor: i===selIdx ? '#1677ff' : '#aab6c6',
        strokeWeight: i===selIdx ? 9 : 6,
        strokeOpacity: i===selIdx ? 0.95 : 0.5,
        zIndex: i===selIdx ? 60 : 40, lineJoin:'round', lineCap:'round',
        showDir: i===selIdx, isOutline:i===selIdx, outlineColor:'#ffffff', borderWeight:i===selIdx?2:0 });
      pl.on('click', (function(idx){ return function(){ selectRoute(idx); }; })(i));
      pl.setMap(map); overlays.push(pl);
    });
    addEndpoints();
    renderCards();
    $('planPanel').classList.remove('hidden');
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
      var c=document.createElement('div'); c.className='rcard'+(i===selIdx?' on':'');
      c.innerHTML=tag+'<div class="rt">'+fmtTime(r.time)+'</div><div class="rd">'+fmtDist(r.distance)+extra+'</div><div class="rl">'+(r.label||('路线'+(i+1)))+'</div>';
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

  // ── 导航：真实 GPS 跟随为主（吸附/偏航重算/到达判定/实测车速），无有效信号回退模拟巡航 ──
  function stopGpsWatch(){
    try{ if(nav.watchGeo && nav.watchId!=null) nav.watchGeo.clearWatch(nav.watchId); }catch(e){}
    nav.watchGeo=null; nav.watchId=null;
    if(nav.staleTimer){ clearInterval(nav.staleTimer); nav.staleTimer=0; }
  }
  function setSpeed(kmh){ $('speedVal').textContent = (kmh>=0) ? ''+Math.round(kmh) : '--'; nav.speedKmh=kmh; }

  // 一次定位吸附到路线：从当前索引附近窗口找最近路径点（i=索引，off=偏离米数）
  function snapToRoute(p){
    var a=Math.max(0, Math.floor(nav.ci)-20), b=Math.min(nav.full.length-1, Math.floor(nav.ci)+400);
    var best=a, bestD=1/0;
    for(var k=a;k<=b;k++){ var d0=dist(p, nav.full[k]); if(d0<bestD){ bestD=d0; best=k; } }
    return { i:best, off:bestD };
  }

  // 导航 UI 统一刷新（GPS/模拟两种驱动共用）：位置/朝向/剩余/ETA/进度/转向卡/播报
  function navUpdate(i, posOverride){
    var pos = posOverride || nav.full[i];
    if(nav.carMk){ nav.carMk.setPosition(pos);
      if(i>0){ try{ nav.carMk.setAngle(bearing(nav.full[i-1], nav.full[i])); }catch(e){} } }
    try{ map.setCenter(pos); }catch(e){}
    var rem=nav.suffix[i]||0;
    var remTime=nav.totalDist>0 ? nav.totalTime*(rem/nav.totalDist) : 0;
    $('navRemain').textContent='剩余 '+fmtDist(rem)+' · '+fmtTime(remTime);
    var eta=new Date(Date.now()+remTime*1000);
    $('navEta').textContent='预计 '+pad(eta.getHours())+':'+pad(eta.getMinutes())+' 到达';
    $('navProgFill').style.width=(nav.totalDist>0 ? (100*(nav.totalDist-rem)/nav.totalDist) : 0)+'%';
    var si=0; for(var k=0;k<nav.stepIdx.length;k++){ if(nav.stepIdx[k]<=i) si=k; else break; }
    if(si>nav.announced){ nav.announced=si; announceStepUI(si); speakStep(si); }
    else { updateTurnDist(si); }
  }

  // 装载路线数据 + 重绘导航覆盖物（startNav 与偏航重算共用）
  function beginNavUI(r){
    nav.full=r.path; nav.totalDist=r.distance; nav.totalTime=r.time;
    nav.seg=[]; nav.suffix=new Array(r.path.length); nav.suffix[r.path.length-1]=0;
    for(var k=0;k<r.path.length-1;k++) nav.seg[k]=dist(r.path[k],r.path[k+1]);
    var acc=0; for(var k2=r.path.length-2;k2>=0;k2--){ acc+=nav.seg[k2]; nav.suffix[k2]=acc; }
    nav.stepIdx=r.steps.map(function(s){return s.idx;});
    nav.stepInstr=r.steps.map(function(s){return s.instr;});
    nav.stepAction=r.steps.map(function(s){return s.action;});
    nav.stepRoad=r.steps.map(function(s){return s.road;});
    nav.stepDist=r.steps.map(function(s){return s.dist;});
    nav.ci=0; nav.announced=0; nav.offCnt=0; nav.rerouting=false; nav.arrived=false;
    nav.destLL=r.path[r.path.length-1];

    clearOverlays();
    var pl=new AMap.Polyline({ path:r.path, strokeColor:'#1677ff', strokeWeight:10, strokeOpacity:0.95,
      zIndex:60, lineJoin:'round', lineCap:'round', showDir:true, isOutline:true, outlineColor:'#fff', borderWeight:2 });
    pl.setMap(map); overlays.push(pl);
    nav.carMk=new AMap.Marker({ position:r.path[0], zIndex:130, anchor:'center', angle:0,
      icon:new AMap.Icon({ image:ARROW_SVG, size:new AMap.Size(46,46), imageSize:new AMap.Size(46,46) }) });
    nav.carMk.setMap(map); overlays.push(nav.carMk);
    map.setZoomAndCenter(16, r.path[0]);

    $('navProgFill').style.width='0%';
    $('navRemain').textContent='剩余 '+fmtDist(r.distance)+' · '+fmtTime(r.time); $('navEta').textContent='';
    announceStepUI(0);
  }

  function startNav(){
    var r=routes[selIdx];
    if(!r){ toast('请先选择路线'); return; }
    if(r.bus){ toast('公交暂不支持导航，请选驾车/步行/骑行'); return; }
    if(!r.path.length){ toast('该路线无可导航路径'); return; }

    nav.on=true; nav.mode='gps'; nav.goodFix=false; nav.lastFixPos=null; nav.lastFixTime=0; nav.lastProc=0;
    setSpeed(-1);

    $('topbar').classList.add('hidden'); $('planPanel').classList.add('hidden');
    $('voiceFab').classList.add('hidden'); $('loc').classList.add('hidden');
    $('navTop').classList.remove('hidden'); $('navBottom').classList.remove('hidden');
    $('speedBall').classList.remove('hidden');

    beginNavUI(r);
    sendHost('app://nav?open=1&t='+Date.now());
    speakLines(['导航开始，全程'+fmtDist(r.distance)+'，预计'+fmtTime(r.time)+'，请遵守交规，注意行车安全。']);
    startGpsWatch();
  }

  // 启动实时定位跟随；8 秒内拿不到有效精度定位（无定位硬件/仅 IP 级）→ 回退模拟巡航
  function startGpsWatch(){
    stopGpsWatch();
    try{
      nav.watchGeo=new AMap.Geolocation({ enableHighAccuracy:true, timeout:6000, maximumAge:1000 });
      try{ nav.watchGeo.on('complete', function(result){ onGpsFix('complete', result); }); }catch(e2){}
      nav.watchId=nav.watchGeo.watchPosition(function(status,result){ onGpsFix(status,result); });
    }catch(e){ nav.watchGeo=null; }
    if(!nav.watchGeo){ fallbackToSim(); return; }
    setTimeout(function(){ if(nav.on && nav.mode==='gps' && !nav.goodFix) fallbackToSim(); }, 8000);
    // 行进中信号丢失提醒（真实导航必要反馈）
    nav.staleTimer=setInterval(function(){
      if(!nav.on || nav.mode!=='gps' || nav.arrived) return;
      if(nav.goodFix && nav.lastFixTime>0 && Date.now()-nav.lastFixTime>15000) toast('定位信号弱，等待恢复…');
    }, 10000);
  }

  function onGpsFix(status,result){
    if(!nav.on || nav.arrived || nav.mode!=='gps') return;
    if(status!=='complete' || !result || !result.position) return;
    var acc=result.accuracy||9999;
    if(result.location_type==='ip' || acc>300) return;   // IP 级/超差精度不可用于导航
    var now=Date.now();
    if(now-nav.lastProc<300) return; nav.lastProc=now;   // 双通道回调去抖
    nav.goodFix=true;
    var p=result.position;
    // 车速：优先设备上报，否则按位移/时差推算
    var kmh=-1;
    if(typeof result.speed==='number' && result.speed>=0) kmh=result.speed*3.6;
    else if(nav.lastFixPos && now>nav.lastFixTime){
      var dt=(now-nav.lastFixTime)/1000; if(dt>=0.8) kmh=dist(nav.lastFixPos,p)/dt*3.6;
    }
    if(kmh>=0) setSpeed(Math.min(kmh,199));
    if(!nav.lastFixPos || now-nav.lastFixTime>=800){ nav.lastFixPos=p; nav.lastFixTime=now; }

    var s=snapToRoute(p);
    if(s.off>60){
      nav.offCnt++;
      if(nav.offCnt>=3){ reroute(p); return; }
    } else nav.offCnt=0;
    if(s.i>nav.ci) nav.ci=s.i;   // 只前进不回跳，避免 GPS 抖动
    var i=Math.floor(nav.ci);
    navUpdate(i, (s.off<=25) ? nav.full[i] : p);   // 贴路吸附显示；偏差大时如实显示真实位置
    if((nav.suffix[i]||0)<30 && s.off<60) arrive();
  }

  // 偏航自动重算：以当前位置为起点重新算路并继续导航
  function reroute(fromPos){
    if(nav.rerouting || !nav.destLL) return;
    nav.rerouting=true; nav.offCnt=0;
    toast('已偏离路线，正在重新规划…');
    speakLines(['您已偏离路线，正在为您重新规划。']);
    var svc=null;
    try{
      svc = curMode==='walk' ? new AMap.Walking()
          : curMode==='ride' ? new AMap.Riding()
          : new AMap.Driving({ policy:drivingPolicies()[0] });
    }catch(e){ nav.rerouting=false; return; }
    svc.search(fromPos, nav.destLL, function(status,result){
      nav.rerouting=false;
      if(!nav.on || nav.arrived || nav.mode!=='gps') return;
      if(status==='complete' && result.routes && result.routes.length){
        var n=toNormalized(result.routes[0]);
        if(n){ beginNavUI(n); speakLines(['已为您重新规划路线，全程'+fmtDist(n.distance)+'。']); return; }
      }
      toast('重新规划失败，请回到原路线');
    });
  }

  // 无有效定位信号 → 回退模拟巡航（明确提示；车速显示路线真实均速）
  function fallbackToSim(){
    if(!nav.on || nav.mode!=='gps') return;
    stopGpsWatch();
    nav.mode='sim';
    toast('未获取到有效定位信号，已切换为模拟巡航');
    speakLines(['未获取到定位信号，已为您切换到模拟巡航。']);
    setSpeed(nav.totalTime>0 ? nav.totalDist/nav.totalTime*3.6 : 40);
    var simSec=Math.min(60, Math.max(15, nav.totalDist/400));
    var tickMs=160, stepF=nav.full.length/(simSec*1000/tickMs);
    nav.timer=setInterval(function(){ simTick(stepF); }, tickMs);
  }

  function simTick(stepN){
    if(!nav.on || nav.arrived) return;
    nav.ci=Math.min(nav.full.length-1, nav.ci+stepN);
    var i=Math.floor(nav.ci);
    navUpdate(i);
    if(nav.ci>=nav.full.length-1) arrive();
  }

  function arrive(){
    if(!nav.on || nav.arrived) return;
    nav.arrived=true;
    if(nav.timer){ clearInterval(nav.timer); nav.timer=0; }
    stopGpsWatch();
    setSpeed(0);
    $('turnIco').textContent='🏁'; $('turnInstr').textContent='已到达目的地'; $('turnDist').innerHTML='';
    $('navRemain').textContent='已到达目的地'; $('navEta').textContent=''; $('navProgFill').style.width='100%';
    speakLines(['您已到达目的地附近，本次导航结束。']);
  }

  function exitNav(){
    if(!nav.on && !nav.timer) return;
    if(nav.timer){ clearInterval(nav.timer); nav.timer=0; }
    stopGpsWatch();
    nav.on=false; nav.arrived=false;
    try{ if('speechSynthesis' in window) speechSynthesis.cancel(); }catch(e){}
    $('navTop').classList.add('hidden'); $('navBottom').classList.add('hidden'); $('speedBall').classList.add('hidden');
    $('topbar').classList.remove('hidden'); $('voiceFab').classList.remove('hidden'); $('loc').classList.remove('hidden');
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
  document.querySelectorAll('.io-loc').forEach(function(b){
    b.addEventListener('click', function(){ locateInto(b.getAttribute('data-t')); });
  });
  $('go').addEventListener('click', plan);
  $('loc').addEventListener('click', recenter);
  $('swap').addEventListener('click', function(){ var a=$('from').value; $('from').value=$('to').value; $('to').value=a; var t=fromLL; fromLL=toLL; toLL=t; });
  $('from').addEventListener('input', function(){ fromLL=null; });
  $('to').addEventListener('input', function(){ toLL=null; });
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
