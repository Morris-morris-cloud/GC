(function () {
  "use strict";

  const THREE = window.THREE;
  const root = document.getElementById("terrain-annotation");
  const canvas = document.getElementById("terrain-annotation-canvas");
  const callout = document.getElementById("terrain-callout");
  const calloutTitle = document.getElementById("terrain-callout-title");
  const calloutCopy = document.getElementById("terrain-callout-copy");
  const calloutConnector = document.getElementById("terrain-callout-connector");
  const calloutConnectorShape = document.getElementById("terrain-callout-connector-shape");
  const stage = document.querySelector(".media-stage");

  if (!THREE || !root || !canvas || !callout || !calloutTitle || !calloutCopy || !calloutConnector || !calloutConnectorShape || !stage) return;

  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const compactLayout = window.matchMedia("(max-width: 680px)");
  const SOURCE_ASPECT = 1586 / 992;
  const GUIDE_COLOR = "#f6b21a";
  const OUTLINE_COLOR = 0x082742;
  const TARGET_OUTER_RADIUS = 20;
  const ROLE_STYLES = {
    main: { width: 8, outline: 14, opacity: 1, dashed: 0, dashCount: 1, dashRatio: 1, dashSpeed: 0 },
    branch: { width: 6.5, outline: 11.5, opacity: 1, dashed: 1, dashCount: 4, dashRatio: 0.62, dashSpeed: 0 },
    ring: { width: 6, outline: 10.5, opacity: 1, dashed: 0, dashCount: 1, dashRatio: 1, dashSpeed: 0 },
    contour: { width: 4.5, outline: 8.5, opacity: 1, dashed: 0, dashCount: 1, dashRatio: 1, dashSpeed: 0 },
    bracket: { width: 5.5, outline: 10, opacity: 1, dashed: 1, dashCount: 4, dashRatio: 0.62, dashSpeed: 0 }
  };

  const ANNOTATIONS = {
    peak: {
      title: "山峰 · 最高中心",
      copy: "闭合等高线由外向内数值升高，中心位置为山峰。",
      color: GUIDE_COLOR,
      perspective: {
        traces: [
          { role: "main", arrow: true, points: [[0.628, 0.575], [0.650, 0.455], [0.670, 0.280], [0.709, 0.143]] }
        ],
        target: [0.709, 0.143],
        placement: { side: "right", dx: 34, dy: 24 }
      },
      top: {
        traces: [
          { role: "main", arrow: true, points: [[0.575, 0.645], [0.610, 0.555], [0.645, 0.485], [0.668, 0.449]] }
        ],
        target: [0.668, 0.449],
        placement: { side: "right", dx: 34, dy: -10 }
      }
    },
    ridge: {
      title: "山脊 · 分水脊线",
      copy: "等高线向低处凸出，脊线两侧水流向外分开。",
      color: GUIDE_COLOR,
      perspective: {
        traces: [
          { role: "main", arrow: true, points: [[0.765, 0.245], [0.720, 0.365], [0.675, 0.500], [0.625, 0.645]] },
          { role: "branch", arrow: true, points: [[0.675, 0.500], [0.635, 0.535], [0.595, 0.565]] },
          { role: "branch", arrow: true, points: [[0.675, 0.500], [0.715, 0.535], [0.755, 0.570]] }
        ],
        target: [0.675, 0.500],
        placement: { side: "left", dx: -34, dy: -85 }
      },
      top: {
        traces: [
          { role: "main", arrow: true, points: [[0.690, 0.270], [0.655, 0.395], [0.620, 0.520], [0.590, 0.650]] },
          { role: "branch", arrow: true, points: [[0.620, 0.520], [0.582, 0.545], [0.548, 0.575]] },
          { role: "branch", arrow: true, points: [[0.620, 0.520], [0.658, 0.548], [0.695, 0.580]] }
        ],
        target: [0.620, 0.520],
        placement: { side: "left", dx: -34, dy: -85 }
      }
    },
    valley: {
      title: "山谷 · 汇水谷线",
      copy: "V形尖端指向上游，水流沿谷底向下游汇集。",
      color: GUIDE_COLOR,
      perspective: {
        traces: [
          { role: "main", arrow: true, points: [[0.690, 0.410], [0.678, 0.520], [0.676, 0.550], [0.660, 0.650], [0.640, 0.790]] },
          { role: "branch", arrow: true, points: [[0.590, 0.560], [0.630, 0.560], [0.676, 0.550]] },
          { role: "branch", arrow: true, points: [[0.750, 0.560], [0.710, 0.560], [0.676, 0.550]] }
        ],
        target: [0.676, 0.550],
        placement: { side: "right", dx: 34, dy: -80 }
      },
      top: {
        traces: [
          { role: "main", arrow: true, points: [[0.600, 0.355], [0.565, 0.470], [0.525, 0.585], [0.470, 0.690]] },
          { role: "branch", arrow: true, points: [[0.455, 0.520], [0.490, 0.555], [0.525, 0.585]] },
          { role: "branch", arrow: true, points: [[0.600, 0.510], [0.565, 0.550], [0.525, 0.585]] }
        ],
        target: [0.525, 0.585],
        placement: { side: "right", dx: 34, dy: 70 }
      }
    },
    saddle: {
      title: "鞍部 · 两峰低口",
      copy: "两组闭合等高线之间的低口，就是鞍部。",
      color: GUIDE_COLOR,
      perspective: {
        traces: [
          { role: "main", points: [[0.625, 0.425], [0.675, 0.465], [0.725, 0.420]] }
        ],
        target: [0.675, 0.465],
        placement: { side: "below", dx: 0, dy: 38 }
      },
      top: {
        traces: [
          { role: "main", points: [[0.515, 0.475], [0.560, 0.505], [0.610, 0.475]] }
        ],
        target: [0.560, 0.505],
        placement: { side: "below", dx: 0, dy: 38 }
      }
    },
    cliff: {
      title: "陡崖 · 密集重合带",
      copy: "等高线极密集或重合，表示此处坡度陡峭。",
      color: GUIDE_COLOR,
      perspective: {
        traces: [
          { role: "contour", points: [[0.535, 0.345], [0.565, 0.352], [0.590, 0.360]] },
          { role: "contour", points: [[0.532, 0.390], [0.562, 0.398], [0.590, 0.405]] },
          { role: "contour", points: [[0.530, 0.435], [0.560, 0.443], [0.590, 0.452]] },
          { role: "main", arrow: true, points: [[0.595, 0.305], [0.590, 0.405], [0.598, 0.510]] }
        ],
        target: [0.590, 0.405],
        placement: { side: "right", dx: 34, dy: 0 }
      },
      top: {
        traces: [
          { role: "main", arrow: true, points: [[0.535, 0.560], [0.585, 0.520], [0.635, 0.480]] },
          { role: "contour", points: [[0.555, 0.455], [0.575, 0.515], [0.595, 0.575]] },
          { role: "contour", points: [[0.590, 0.435], [0.610, 0.495], [0.630, 0.555]] }
        ],
        target: [0.585, 0.520],
        placement: { side: "below", dx: 0, dy: 70 }
      }
    }
  };

  let renderer;
  let scene;
  let camera;
  let annotationGroup;
  let aspect = 1;
  let stageHeight = 1;
  let animationFrame = 0;
  let revealStart = 0;
  let calloutTimer = 0;
  let hideTimer = 0;
  let currentKey = null;
  let currentMode = "perspective";
  let buildToken = 0;
  let animatedMaterials = [];
  let revealGeometries = [];
  let pulseMeshes = [];
  let delayedMarkers = [];
  let runnerDots = [];

  try {
    renderer = new THREE.WebGLRenderer({ canvas: canvas, alpha: true, antialias: true, powerPreference: "high-performance" });
    renderer.setClearColor(0x000000, 0);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.75));
    scene = new THREE.Scene();
    camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0.1, 10);
    camera.position.z = 4;
  } catch (error) {
    root.dataset.renderer = "unavailable";
    return;
  }

  function sourceToScreen(point) {
    let renderedWidth = 1;
    let renderedHeight = 1;
    let offsetX = 0;
    let offsetY = 0;
    if (compactLayout.matches) {
      if (aspect >= SOURCE_ASPECT) {
        renderedWidth = SOURCE_ASPECT / aspect;
        offsetX = (1 - renderedWidth) / 2;
      } else {
        renderedHeight = aspect / SOURCE_ASPECT;
        offsetY = (1 - renderedHeight) / 2;
      }
    } else {
      if (aspect >= SOURCE_ASPECT) {
        renderedHeight = aspect / SOURCE_ASPECT;
        offsetY = (1 - renderedHeight) / 2;
      } else {
        renderedWidth = SOURCE_ASPECT / aspect;
        offsetX = (1 - renderedWidth) / 2;
      }
    }
    return [offsetX + point[0] * renderedWidth, offsetY + point[1] * renderedHeight];
  }

  function toWorld(point) {
    const screenPoint = sourceToScreen(point);
    return new THREE.Vector3((screenPoint[0] - 0.5) * 2 * aspect, (0.5 - screenPoint[1]) * 2, 0);
  }

  function getVisualScale() {
    return Math.max(0.72, Math.min(1, stage.clientWidth / 1200));
  }

  function disposeMaterial(material) {
    if (Array.isArray(material)) material.forEach(disposeMaterial);
    else if (material && typeof material.dispose === "function") material.dispose();
  }

  function clearAnnotation() {
    if (!annotationGroup) return;
    annotationGroup.traverse(function (object) {
      if (object.geometry) object.geometry.dispose();
      if (object.material) disposeMaterial(object.material);
    });
    scene.remove(annotationGroup);
    annotationGroup = null;
    animatedMaterials = [];
    revealGeometries = [];
    pulseMeshes = [];
    delayedMarkers = [];
    runnerDots = [];
  }

  function createRibbonGeometry(samples, widthInPixels) {
    const positions = [];
    const uvs = [];
    const indices = [];
    const halfWidth = widthInPixels / Math.max(1, stageHeight);
    samples.forEach(function (point, index) {
      const previous = samples[Math.max(0, index - 1)];
      const next = samples[Math.min(samples.length - 1, index + 1)];
      const tangentX = next.x - previous.x;
      const tangentY = next.y - previous.y;
      const length = Math.hypot(tangentX, tangentY) || 1;
      const normalX = -tangentY / length;
      const normalY = tangentX / length;
      const progress = index / Math.max(1, samples.length - 1);
      positions.push(point.x + normalX * halfWidth, point.y + normalY * halfWidth, point.z);
      positions.push(point.x - normalX * halfWidth, point.y - normalY * halfWidth, point.z);
      uvs.push(progress, 0, progress, 1);
      if (index < samples.length - 1) {
        const base = index * 2;
        indices.push(base, base + 1, base + 2, base + 1, base + 3, base + 2);
      }
    });
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
    geometry.setAttribute("uv", new THREE.Float32BufferAttribute(uvs, 2));
    geometry.setIndex(indices);
    geometry.computeBoundingSphere();
    return geometry;
  }

  function makeLineMaterial(color, style) {
    return new THREE.ShaderMaterial({
      uniforms: {
        uTime: { value: 0 },
        uColor: { value: new THREE.Color(color) },
        uOpacity: { value: style.opacity },
        uMotion: { value: reducedMotion.matches ? 0 : 1 },
        uDashed: { value: style.dashed },
        uDashCount: { value: style.dashCount },
        uDashRatio: { value: style.dashRatio },
        uDashSpeed: { value: style.dashSpeed }
      },
      vertexShader: [
        "varying vec2 vUv;",
        "void main() {",
        "  vUv = uv;",
        "  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);",
        "}"
      ].join("\n"),
      fragmentShader: [
        "uniform float uTime;",
        "uniform vec3 uColor;",
        "uniform float uOpacity;",
        "uniform float uMotion;",
        "uniform float uDashed;",
        "uniform float uDashCount;",
        "uniform float uDashRatio;",
        "uniform float uDashSpeed;",
        "varying vec2 vUv;",
        "void main() {",
        "  float edge = 1.0 - smoothstep(0.43, 0.50, abs(vUv.y - 0.5));",
        "  float dashPhase = fract(vUv.x * uDashCount - uTime * uDashSpeed * uMotion);",
        "  float dash = 1.0 - smoothstep(uDashRatio - 0.04, uDashRatio + 0.04, dashPhase);",
        "  float pattern = mix(1.0, dash, uDashed);",
        "  float alpha = uOpacity * edge * pattern;",
        "  gl_FragColor = vec4(uColor, alpha);",
        "}"
      ].join("\n"),
      transparent: true,
      depthTest: false,
      depthWrite: false,
      blending: THREE.NormalBlending
    });
  }

  function createArrow(points, color, sizeInPixels, delay, stopBeforePixels) {
    const end = toWorld(points[points.length - 1]);
    const before = toWorld(points[points.length - 2]);
    const direction = end.clone().sub(before).normalize();
    const angle = Math.atan2(direction.y, direction.x);
    const unit = 2 / Math.max(1, stageHeight);
    const visualScale = getVisualScale();
    const scaledSize = sizeInPixels * visualScale;
    const arrowPosition = end.clone().addScaledVector(direction, -(stopBeforePixels || 0) * visualScale * unit);
    function createArrowGeometry(pixelSize) {
      const size = pixelSize * unit;
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute("position", new THREE.Float32BufferAttribute([
        0, 0, 0,
        -size * 1.90, size * 0.88, 0,
        -size * 1.90, -size * 0.88, 0
      ], 3));
      return geometry;
    }
    const outlineMaterial = new THREE.MeshBasicMaterial({ color: OUTLINE_COLOR, transparent: true, opacity: 1, depthTest: false, depthWrite: false });
    const material = new THREE.MeshBasicMaterial({ color: color, transparent: true, opacity: 1, depthTest: false, depthWrite: false });
    const outline = new THREE.Mesh(createArrowGeometry(scaledSize + 2.5 * visualScale), outlineMaterial);
    const arrow = new THREE.Mesh(createArrowGeometry(scaledSize), material);
    outline.position.copy(arrowPosition).addScaledVector(direction, 1.25 * visualScale * unit);
    arrow.position.copy(arrowPosition);
    outline.rotation.z = angle;
    arrow.rotation.z = angle;
    outline.renderOrder = 40;
    arrow.renderOrder = 41;
    annotationGroup.add(outline, arrow);
    delayedMarkers.push({ mesh: outline, delay: delay + 0.46 });
    delayedMarkers.push({ mesh: arrow, delay: delay + 0.46 });
  }

  function createRunnerDots(curve, delay, count) {
    const pixelUnit = 2 / Math.max(1, stageHeight);
    const visualScale = getVisualScale();
    const phases = count === 1 ? [0.18] : count === 2 ? [0.18, 0.70] : [0.12, 0.45, 0.78];
    phases.forEach(function (phase, index) {
      const glowMaterial = new THREE.MeshBasicMaterial({
        color: 0xffffff,
        transparent: true,
        opacity: 0.28,
        depthTest: false,
        depthWrite: false,
        blending: THREE.AdditiveBlending
      });
      const dotMaterial = new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 1, depthTest: false, depthWrite: false });
      const glow = new THREE.Mesh(new THREE.CircleGeometry(11 * visualScale * pixelUnit, 40), glowMaterial);
      const dot = new THREE.Mesh(new THREE.CircleGeometry(7.5 * visualScale * pixelUnit, 40), dotMaterial);
      const position = curve.getPointAt(phase);
      glow.position.copy(position);
      dot.position.copy(position);
      glow.renderOrder = 44;
      dot.renderOrder = 45;
      glow.visible = false;
      dot.visible = false;
      annotationGroup.add(glow, dot);
      runnerDots.push({ glow: glow, dot: dot, curve: curve, phase: phase, delay: delay + 0.18, pulseOffset: index * 1.8 });
    });
  }

  function createTrace(trace, color, index, target) {
    const style = ROLE_STYLES[trace.role] || ROLE_STYLES.main;
    const worldPoints = trace.points.map(toWorld);
    const curve = new THREE.CatmullRomCurve3(worldPoints, Boolean(trace.closed), "centripetal", 0.5);
    const pathLengthPixels = curve.getLength() * stageHeight / 2;
    const samples = curve.getSpacedPoints(Math.max(36, trace.points.length * 12));
    const visualScale = getVisualScale();
    const outlineGeometry = createRibbonGeometry(samples, style.outline * visualScale);
    const lineGeometry = createRibbonGeometry(samples, style.width * visualScale);
    const outlineMaterial = makeLineMaterial(OUTLINE_COLOR, style);
    const lineMaterial = makeLineMaterial(color, style);
    const outline = new THREE.Mesh(outlineGeometry, outlineMaterial);
    const line = new THREE.Mesh(lineGeometry, lineMaterial);
    const isMain = trace.role === "main";
    outline.renderOrder = isMain ? 30 : 20;
    line.renderOrder = isMain ? 31 : 21;
    annotationGroup.add(outline, line);
    animatedMaterials.push(outlineMaterial, lineMaterial);
    const delay = index * 0.075;
    if (trace.role === "main") createRunnerDots(curve, delay, pathLengthPixels < 180 ? 1 : pathLengthPixels < 320 ? 2 : 3);
    revealGeometries.push({ geometry: outlineGeometry, count: outlineGeometry.index.count, delay: delay });
    revealGeometries.push({ geometry: lineGeometry, count: lineGeometry.index.count, delay: delay });
    if (trace.arrow && !trace.closed) {
      const endPoint = trace.points[trace.points.length - 1];
      const endsAtTarget = target && Math.hypot(endPoint[0] - target[0], endPoint[1] - target[1]) < 0.002;
      const arrowScale = Math.max(0.82, Math.min(1, pathLengthPixels / 240));
      createArrow(trace.points, color, (trace.role === "branch" ? 14 : 15) * arrowScale, delay, endsAtTarget ? TARGET_OUTER_RADIUS + 4 : 0);
    }
  }

  function createTarget(point, color) {
    const position = toWorld(point);
    const pixelUnit = 2 / Math.max(1, stageHeight);
    const visualScale = getVisualScale();
    const halo = new THREE.Mesh(
      new THREE.CircleGeometry(25 * visualScale * pixelUnit, 64),
      new THREE.MeshBasicMaterial({ color: color, transparent: true, opacity: 0.20, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending })
    );
    const outer = new THREE.Mesh(
      new THREE.CircleGeometry(TARGET_OUTER_RADIUS * visualScale * pixelUnit, 64),
      new THREE.MeshBasicMaterial({ color: OUTLINE_COLOR, transparent: true, opacity: 1, depthTest: false, depthWrite: false })
    );
    const gold = new THREE.Mesh(
      new THREE.CircleGeometry(17.5 * visualScale * pixelUnit, 64),
      new THREE.MeshBasicMaterial({ color: color, transparent: true, opacity: 1, depthTest: false, depthWrite: false })
    );
    const white = new THREE.Mesh(
      new THREE.CircleGeometry(13.5 * visualScale * pixelUnit, 64),
      new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 1, depthTest: false, depthWrite: false })
    );
    const inner = new THREE.Mesh(
      new THREE.CircleGeometry(10 * visualScale * pixelUnit, 56),
      new THREE.MeshBasicMaterial({ color: OUTLINE_COLOR, transparent: true, opacity: 1, depthTest: false, depthWrite: false })
    );
    const core = new THREE.Mesh(
      new THREE.CircleGeometry(7.5 * visualScale * pixelUnit, 56),
      new THREE.MeshBasicMaterial({ color: color, transparent: true, opacity: 1, depthTest: false, depthWrite: false })
    );
    [halo, outer, gold, white, inner, core].forEach(function (mesh, index) {
      mesh.position.copy(position);
      mesh.renderOrder = 49 + index;
      annotationGroup.add(mesh);
    });
    pulseMeshes.push(halo);
  }

  function updateCallout(definition, view) {
    const targetScreen = sourceToScreen(view.target);
    const placement = view.placement;
    calloutTitle.textContent = definition.title + (currentMode === "top" ? " · 俯视判读" : "");
    calloutCopy.textContent = definition.copy;
    const width = stage.clientWidth;
    const height = stage.clientHeight;
    const targetX = targetScreen[0] * width;
    const targetY = targetScreen[1] * height;
    const cardWidth = callout.offsetWidth || Math.min(width <= 680 ? 210 : width <= 920 ? 250 : 280, width - 28);
    const cardHeight = callout.offsetHeight || 88;
    let side = placement.side;
    let x = targetX + placement.dx;
    let y = targetY + placement.dy;
    const targetGap = TARGET_OUTER_RADIUS * getVisualScale() + 18;
    const horizontalGap = Math.max(Math.abs(placement.dx), targetGap);
    const verticalGap = Math.max(Math.abs(placement.dy), targetGap);

    if (side === "right") x = targetX + horizontalGap;
    else if (side === "left") x = targetX - horizontalGap;
    else if (side === "below") y = targetY + verticalGap;
    else if (side === "above") y = targetY - verticalGap;

    if (side === "right" && x + cardWidth > width - 14) {
      side = "left";
      x = targetX - horizontalGap;
    } else if (side === "left" && x - cardWidth < 14) {
      side = "right";
      x = targetX + horizontalGap;
    } else if (side === "below" && y + cardHeight > height - 14) {
      side = "above";
      y = targetY - verticalGap;
    } else if (side === "above" && y - cardHeight < 14) {
      side = "below";
      y = targetY + verticalGap;
    }

    if (side === "right" || side === "left") {
      const horizontalMargin = compactLayout.matches ? 0 : 14;
      if (side === "right") x = Math.max(horizontalMargin, Math.min(width - cardWidth - horizontalMargin, x));
      else x = Math.max(cardWidth + horizontalMargin, Math.min(width - horizontalMargin, x));
      y = Math.max(cardHeight / 2 + 14, Math.min(height - cardHeight / 2 - 14, y));
    } else {
      x = Math.max(cardWidth / 2 + 14, Math.min(width - cardWidth / 2 - 14, x));
    }

    const connectorBaseHalf = 28 * getVisualScale();
    let anchorX = x;
    let anchorY = y;
    let baseOne;
    let baseTwo;
    if (side === "right" || side === "left") {
      const verticalLimit = Math.max(0, cardHeight / 2 - connectorBaseHalf - 4);
      anchorY = y + Math.max(-verticalLimit, Math.min(verticalLimit, targetY - y));
      baseOne = [anchorX, anchorY - connectorBaseHalf];
      baseTwo = [anchorX, anchorY + connectorBaseHalf];
    } else {
      const horizontalLimit = Math.max(0, cardWidth / 2 - connectorBaseHalf - 4);
      anchorX = x + Math.max(-horizontalLimit, Math.min(horizontalLimit, targetX - x));
      baseOne = [anchorX - connectorBaseHalf, anchorY];
      baseTwo = [anchorX + connectorBaseHalf, anchorY];
    }
    const connectorX = anchorX - targetX;
    const connectorY = anchorY - targetY;
    const connectorLength = Math.hypot(connectorX, connectorY) || 1;
    const tipRadius = TARGET_OUTER_RADIUS * getVisualScale() * 0.72;
    const tipX = targetX + connectorX / connectorLength * tipRadius;
    const tipY = targetY + connectorY / connectorLength * tipRadius;
    calloutConnector.setAttribute("viewBox", "0 0 " + width + " " + height);
    calloutConnectorShape.setAttribute(
      "points",
      tipX.toFixed(1) + "," + tipY.toFixed(1) + " " +
      baseOne[0].toFixed(1) + "," + baseOne[1].toFixed(1) + " " +
      baseTwo[0].toFixed(1) + "," + baseTwo[1].toFixed(1)
    );

    const pointerInset = 32;
    const pointerOffsetX = Math.max(-cardWidth / 2 + pointerInset, Math.min(cardWidth / 2 - pointerInset, targetX - x));
    const pointerOffsetY = Math.max(-cardHeight / 2 + pointerInset, Math.min(cardHeight / 2 - pointerInset, targetY - y));
    root.style.setProperty("--callout-x", x + "px");
    root.style.setProperty("--callout-y", y + "px");
    root.style.setProperty("--pointer-offset-x", pointerOffsetX + "px");
    root.style.setProperty("--pointer-offset-y", pointerOffsetY + "px");
    root.dataset.side = side;
    root.dataset.tone = currentKey === "cliff" ? "warning" : "default";
  }

  function buildAnnotation(key, mode, animate) {
    const definition = ANNOTATIONS[key];
    if (!definition) return;
    const view = definition[mode === "top" ? "top" : "perspective"];
    clearAnnotation();
    annotationGroup = new THREE.Group();
    scene.add(annotationGroup);
    view.traces.forEach(function (trace, index) { createTrace(trace, definition.color, index, view.target); });
    createTarget(view.target, definition.color);
    updateCallout(definition, view);
    revealGeometries.forEach(function (item) {
      item.geometry.setDrawRange(0, animate && !reducedMotion.matches ? 0 : item.count);
    });
    delayedMarkers.forEach(function (item) { item.mesh.visible = !animate || reducedMotion.matches; });
    runnerDots.forEach(function (item) {
      const position = item.curve.getPointAt(item.phase);
      item.glow.position.copy(position);
      item.dot.position.copy(position);
      item.glow.visible = !animate || reducedMotion.matches;
      item.dot.visible = !animate || reducedMotion.matches;
    });
    revealStart = performance.now() - (animate && !reducedMotion.matches ? 0 : 760);
  }

  function resize() {
    const width = Math.max(1, stage.clientWidth);
    const height = Math.max(1, stage.clientHeight);
    const nextAspect = width / height;
    const sizeChanged = Math.abs(nextAspect - aspect) > 0.001 || Math.abs(height - stageHeight) > 1;
    aspect = nextAspect;
    stageHeight = height;
    renderer.setSize(width, height, false);
    camera.left = -nextAspect;
    camera.right = nextAspect;
    camera.top = 1;
    camera.bottom = -1;
    camera.updateProjectionMatrix();
    if (sizeChanged && currentKey && !root.hidden) buildAnnotation(currentKey, currentMode, false);
    renderer.render(scene, camera);
  }

  function renderFrame(now) {
    if (!currentKey || root.hidden || document.hidden) {
      animationFrame = 0;
      return;
    }
    const seconds = now * 0.001;
    const elapsed = (now - revealStart) / 760;
    animatedMaterials.forEach(function (material) {
      material.uniforms.uTime.value = seconds;
      material.uniforms.uMotion.value = reducedMotion.matches ? 0 : 1;
    });
    if (!reducedMotion.matches) {
      revealGeometries.forEach(function (item) {
        const progress = Math.max(0, Math.min(1, (elapsed - item.delay) / 0.62));
        const eased = 1 - Math.pow(1 - progress, 3);
        const count = Math.floor(item.count * eased / 6) * 6;
        item.geometry.setDrawRange(0, Math.max(0, count));
      });
      delayedMarkers.forEach(function (item) { item.mesh.visible = elapsed >= item.delay; });
      const motionSeconds = Math.max(0, (now - revealStart) * 0.001);
      runnerDots.forEach(function (item) {
        const visible = elapsed >= item.delay;
        item.glow.visible = visible;
        item.dot.visible = visible;
        if (!visible) return;
        const position = item.curve.getPointAt((item.phase + motionSeconds * 0.065) % 1);
        item.glow.position.copy(position);
        item.dot.position.copy(position);
        item.glow.scale.setScalar(1 + Math.sin(seconds * 3.6 + item.pulseOffset) * 0.14);
      });
      const pulse = 1 + Math.sin(seconds * 3.2) * 0.10;
      pulseMeshes.forEach(function (mesh) { mesh.scale.setScalar(pulse); });
    }
    renderer.render(scene, camera);
    if (reducedMotion.matches) {
      animationFrame = 0;
      return;
    }
    animationFrame = window.requestAnimationFrame(renderFrame);
  }

  function startRendering() {
    if (!animationFrame) animationFrame = window.requestAnimationFrame(renderFrame);
  }

  function show(key, mode) {
    if (!ANNOTATIONS[key]) return;
    window.clearTimeout(calloutTimer);
    window.clearTimeout(hideTimer);
    buildToken += 1;
    currentKey = key;
    currentMode = mode === "top" ? "top" : "perspective";
    root.hidden = false;
    root.classList.remove("is-visible", "is-callout-visible");
    resize();
    buildAnnotation(currentKey, currentMode, true);
    const token = buildToken;
    window.requestAnimationFrame(function () {
      if (token === buildToken) root.classList.add("is-visible");
    });
    calloutTimer = window.setTimeout(function () {
      if (token === buildToken) root.classList.add("is-callout-visible");
    }, reducedMotion.matches ? 0 : 430);
    startRendering();
  }

  function hide(immediate) {
    buildToken += 1;
    currentKey = null;
    window.clearTimeout(calloutTimer);
    window.clearTimeout(hideTimer);
    root.classList.remove("is-visible", "is-callout-visible");
    if (immediate) {
      root.hidden = true;
      clearAnnotation();
      if (animationFrame) {
        window.cancelAnimationFrame(animationFrame);
        animationFrame = 0;
      }
      return;
    }
    hideTimer = window.setTimeout(function () {
      if (!currentKey) {
        root.hidden = true;
        clearAnnotation();
      }
    }, 220);
  }

  if (typeof ResizeObserver === "function") {
    const observer = new ResizeObserver(resize);
    observer.observe(stage);
  } else {
    window.addEventListener("resize", resize, { passive: true });
  }
  const handleMotionPreference = function () {
    if (currentKey) {
      buildAnnotation(currentKey, currentMode, false);
      startRendering();
    }
  };
  if (typeof reducedMotion.addEventListener === "function") reducedMotion.addEventListener("change", handleMotionPreference);
  else if (typeof reducedMotion.addListener === "function") reducedMotion.addListener(handleMotionPreference);
  document.addEventListener("visibilitychange", function () {
    if (!document.hidden && currentKey) startRendering();
  });

  window.TerrainAnnotation = {
    show: show,
    hide: hide,
    getState: function () {
      return { key: currentKey, mode: currentMode, renderer: "three-r" + THREE.REVISION, side: root.dataset.side || null };
    }
  };
})();
