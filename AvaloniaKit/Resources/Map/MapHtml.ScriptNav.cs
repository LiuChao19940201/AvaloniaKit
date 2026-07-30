namespace AvaloniaKit.Resources;

// ── MapHtml 分部：导航脚本（真实 GPS 跟随/偏航重算/到达判定/车速球/把手手势/事件绑定）──
//    紧接 ScriptCorePart，仍处于同一 IIFE 内；末尾闭合 })(); 并结束文档
public static partial class MapHtml
{
    private const string ScriptNavPart = """
  // ── 导航：真实 GPS 跟随（吸附/偏航自动重算/到达判定/实测车速）；无信号如实提示并等待 ──
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
    var brg = i>0 ? bearing(nav.full[i-1], nav.full[i]) : -1;
    if(nav.carMk){ nav.carMk.setPosition(pos);
      // ★ 原生端 heading-up：箭头恒指屏幕上方；内嵌端正北模式：箭头指行进方向
      if(brg>=0){ try{ nav.carMk.setAngle(IS_EMBED ? brg : 0); }catch(e){} } }
    // ★ 行进方向朝前：地图随方位旋转（>6° 才转，避免 GPS 抖动带来的晃动）
    if(!IS_EMBED && brg>=0 && angDiff(brg, nav.lastBrg)>6){
      nav.lastBrg=brg;
      try{ map.setRotation((360-brg)%360); }catch(e){}
    }
    try{ map.setCenter((!IS_EMBED && brg>=0) ? pointAhead(pos, brg, 120) : pos); }catch(e){}
    var rem=nav.suffix[i]||0;
    var remTime=nav.totalDist>0 ? nav.totalTime*(rem/nav.totalDist) : 0;
    $('navRemain').textContent='剩余 '+fmtDist(rem)+' · '+fmtTime(remTime);
    var eta=new Date(Date.now()+remTime*1000);
    $('navEta').textContent='预计 '+pad(eta.getHours())+':'+pad(eta.getMinutes())+' 到达';
    var prog=nav.totalDist>0 ? (100*(nav.totalDist-rem)/nav.totalDist) : 0;
    $('navProgFill').style.width=prog+'%';
    $('railDone').style.height=prog+'%'; $('railCar').style.bottom=prog+'%';
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
    nav.ci=0; nav.announced=0; nav.offCnt=0; nav.rerouting=false; nav.arrived=false; nav.lastBrg=-999;
    nav.destLL=r.path[r.path.length-1];

    clearOverlays();
    drawColoredRoute(r, 60);
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

    nav.on=true; nav.goodFix=false; nav.lastFixPos=null; nav.lastFixTime=0; nav.lastProc=0;
    setSpeed(-1);

    $('topbar').classList.add('hidden'); $('planPanel').classList.add('hidden');
    $('voiceFab').classList.add('hidden'); $('loc').classList.add('hidden');
    $('navTop').classList.remove('hidden'); $('navBottom').classList.remove('hidden');
    $('speedBall').classList.remove('hidden'); $('navRail').classList.remove('hidden');

    beginNavUI(r);
    buildRail(r);
    $('navEta').textContent='正在等待定位信号…';
    sendHost('app://nav?open=1&t='+Date.now());
    chime();
    var etaD=new Date(Date.now()+r.time*1000);
    speakLines(['导航开始，全程'+fmtDist(r.distance)+'，预计行驶'+fmtTime(r.time)+'，'
      +etaD.getHours()+'点'+pad(etaD.getMinutes())+'分左右到达。请遵守交规，注意行车安全。']);
    startGpsWatch();
  }

  // 启动实时定位跟随；无信号时如实提示并持续等待（导航工具不做演示）
  function startGpsWatch(){
    stopGpsWatch();
    try{
      nav.watchGeo=new AMap.Geolocation({ enableHighAccuracy:true, timeout:6000, maximumAge:1000 });
      try{ nav.watchGeo.on('complete', function(result){ onGpsFix('complete', result); }); }catch(e2){}
      nav.watchId=nav.watchGeo.watchPosition(function(status,result){ onGpsFix(status,result); });
    }catch(e){ nav.watchGeo=null; }
    if(!nav.watchGeo){ noSignalHint(); return; }
    setTimeout(function(){ noSignalHint(); }, 8000);
    // 行进中信号丢失提醒（真实导航必要反馈）
    nav.staleTimer=setInterval(function(){
      if(!nav.on || nav.arrived) return;
      if(nav.goodFix && nav.lastFixTime>0 && Date.now()-nav.lastFixTime>15000) toast('定位信号弱，等待恢复…');
    }, 10000);
  }
  function noSignalHint(){
    if(!nav.on || nav.arrived || nav.goodFix) return;
    toast('未获取到定位信号，请检查系统定位服务与权限');
    speakLines(['未获取到定位信号，请检查定位设置。']);
  }

  function onGpsFix(status,result){
    if(!nav.on || nav.arrived) return;
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
          : new AMap.Driving({ policy:drivingPolicies()[0], extensions:'all' });
    }catch(e){ nav.rerouting=false; return; }
    svc.search(fromPos, nav.destLL, function(status,result){
      nav.rerouting=false;
      if(!nav.on || nav.arrived) return;
      if(status==='complete' && result.routes && result.routes.length){
        var n=toNormalized(result.routes[0]);
        if(n){ beginNavUI(n); speakLines(['已为您重新规划路线，全程'+fmtDist(n.distance)+'。']); return; }
      }
      toast('重新规划失败，请回到原路线');
    });
  }

  function arrive(){
    if(!nav.on || nav.arrived) return;
    nav.arrived=true;
    stopGpsWatch();
    setSpeed(0);
    $('turnIco').textContent='🏁'; $('turnInstr').textContent='已到达目的地'; $('turnDist').innerHTML='';
    $('navRemain').textContent='已到达目的地'; $('navEta').textContent=''; $('navProgFill').style.width='100%';
    $('railDone').style.height='100%'; $('railCar').style.bottom='100%';
    chime();
    speakLines(['您已到达目的地附近，本次导航结束。']);
  }

  function exitNav(){
    if(!nav.on) return;
    var wasArrived = nav.arrived;   // 已到达则 arrive() 已播报结束语，主动退出不重复
    stopGpsWatch();
    nav.on=false; nav.arrived=false;
    stopSpeech();
    try{ map.setRotation(0); }catch(e){}   // 退出导航恢复正北朝上
    $('navTop').classList.add('hidden'); $('navBottom').classList.add('hidden');
    $('speedBall').classList.add('hidden'); $('navRail').classList.add('hidden');
    $('topbar').classList.remove('hidden'); $('topbar').classList.remove('collapsed');
    $('voiceFab').classList.remove('hidden'); $('loc').classList.remove('hidden');
    sendHost('app://nav?open=0&t='+Date.now());
    if(routes.length) renderRoutes();
    // ★ 参考高德：中途主动退出给结束提示音 + 语音播报（到达后退出不重复）
    if(!wasArrived){ endChime(); toast('导航已结束'); speakLines(['导航已结束。']); }
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

  // 面板把手：点击切换收起/展开；滑动按方向（顶部上滑收起，底部下滑收起）
  function bindGrab(el, panel, collapseDir){
    var y0=null, touched=false;
    el.addEventListener('touchstart', function(e){ y0=e.touches[0].clientY; }, {passive:true});
    el.addEventListener('touchend', function(e){
      touched=true; setTimeout(function(){ touched=false; }, 400);
      if(y0===null) return;
      var dy=e.changedTouches[0].clientY-y0; y0=null;
      if(Math.abs(dy)<14){ panel.classList.toggle('collapsed'); return; }
      var collapse = collapseDir==='up' ? dy<0 : dy>0;
      panel.classList.toggle('collapsed', collapse);
    }, {passive:true});
    el.addEventListener('click', function(){ if(touched) return; panel.classList.toggle('collapsed'); });
  }
  bindGrab($('topGrab'), $('topbar'), 'up');
  bindGrab($('planGrab'), $('planPanel'), 'down');

  if(document.readyState==='complete') initMap();
  else window.addEventListener('load', initMap);
})();
</script>
</body>
</html>
""";
}
