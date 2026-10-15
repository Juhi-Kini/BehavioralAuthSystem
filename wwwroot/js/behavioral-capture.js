window.behavioralCapture = (() => {

    let area = null;
    let patternType = '';
    let recording = false;

    let clicks = [];
    let path = [];

    let shortcut = {
        ctrl: false,
        shift: false,
        alt: false,
        meta: false,
        key: ''
    };

    let keySequence = [];

    let drawing = false;

    let autoResolve = null;
    let autoTimer = null;
    let sequenceTimer = null;

    // =========================================================
    // COMMON
    // =========================================================

    function point(e) {

        const rect = area?.getBoundingClientRect();

        return {
            x: e.clientX - (rect?.left || 0),
            y: e.clientY - (rect?.top || 0),
            t: performance.now()
        };
    }

    function reset() {

        clicks = [];

        path = [];

        shortcut = {
            ctrl: false,
            shift: false,
            alt: false,
            meta: false,
            key: ''
        };

        keySequence = [];

        drawing = false;
    }

    function clearTimers() {

        if (autoTimer) {
            clearTimeout(autoTimer);
            autoTimer = null;
        }

        if (sequenceTimer) {
            clearTimeout(sequenceTimer);
            sequenceTimer = null;
        }
    }

    // =========================================================
    // MOUSE CLICK
    // =========================================================

    function mouseDownHandler(e) {

        if (!recording || !area)
            return;

        // Only capture actual mouse buttons.
        if (e.button !== 0 && e.button !== 2)
            return;

        e.preventDefault();

        const p = point(e);

        p.button =
            e.button === 2
                ? "Right"
                : "Left";

        clicks.push(p);

        // For automatic verification:
        // after the user stops clicking for a short time,
        // consider the interaction complete.
        scheduleMouseCompletion();
    }

    function contextMenuHandler(e) {

        if (!recording || !area)
            return;

        e.preventDefault();
    }

    function scheduleMouseCompletion() {

        if (patternType !== "Mouse Clicks")
            return;

        if (autoTimer)
            clearTimeout(autoTimer);

        autoTimer = setTimeout(() => {

            if (clicks.length > 0) {

                finishAutomaticCapture();

            }

        }, 750);
    }

    // =========================================================
    // SHAPE
    // =========================================================

    function pointerDownHandler(e) {

        if (!recording ||
            !area ||
            patternType !== "Shape")
            return;

        if (e.button !== 0)
            return;

        e.preventDefault();

        drawing = true;

        path = [];

        path.push(point(e));
    }

    function pointerMoveHandler(e) {

        if (!recording ||
            !area ||
            patternType !== "Shape" ||
            !drawing)
            return;

        e.preventDefault();

        path.push(point(e));
    }

    function pointerUpHandler(e) {

        if (!recording ||
            !area ||
            patternType !== "Shape" ||
            !drawing)
            return;

        e.preventDefault();

        path.push(point(e));

        drawing = false;

        if (path.length >= 5) {

            const shape = classifyShape(path);

            finishAutomaticCapture({
                shapeType: shape
            });
        }
    }

    // =========================================================
    // SHORTCUT
    // =========================================================

    function shortcutKeyHandler(e) {

        if (!recording ||
            patternType !== "Shortcut")
            return;

        if (e.repeat)
            return;

        const key = e.key;

        // Ignore modifier-only keys.
        if (
            key === "Control" ||
            key === "Shift" ||
            key === "Alt" ||
            key === "Meta"
        ) {
            return;
        }

        const hasModifier =
            e.ctrlKey ||
            e.shiftKey ||
            e.altKey ||
            e.metaKey;

        if (!hasModifier)
            return;

        e.preventDefault();

        shortcut = {
            ctrl: e.ctrlKey,
            shift: e.shiftKey,
            alt: e.altKey,
            meta: e.metaKey,
            key: normalizeKey(key)
        };

        finishAutomaticCapture();
    }

    // =========================================================
    // KEY SEQUENCE
    // =========================================================

    function sequenceKeyHandler(e) {

        if (!recording ||
            patternType !== "Key Sequence")
            return;

        if (e.repeat)
            return;

        // Ignore modifier-only keys.
        if (
            e.key === "Control" ||
            e.key === "Shift" ||
            e.key === "Alt" ||
            e.key === "Meta"
        ) {
            return;
        }

        e.preventDefault();

        keySequence.push(
            normalizeKey(e.key)
        );

        if (sequenceTimer)
            clearTimeout(sequenceTimer);

        /*
         * The sequence ends when the user stops typing
         * for about 900 ms.
         */
        sequenceTimer = setTimeout(() => {

            if (keySequence.length >= 2) {

                finishAutomaticCapture();
            }

        }, 900);
    }

    // =========================================================
    // SHAPE CLASSIFICATION
    // =========================================================

    function classifyShape(points) {

        if (!points || points.length < 5)
            return "Line";

        const minX =
            Math.min(...points.map(p => p.x));

        const maxX =
            Math.max(...points.map(p => p.x));

        const minY =
            Math.min(...points.map(p => p.y));

        const maxY =
            Math.max(...points.map(p => p.y));

        const width = Math.max(maxX - minX, 1);

        const height = Math.max(maxY - minY, 1);

        const first = points[0];

        const last =
            points[points.length - 1];

        const closingDistance =
            Math.sqrt(
                Math.pow(last.x - first.x, 2) +
                Math.pow(last.y - first.y, 2)
            );

        const diagonal =
            Math.sqrt(
                width * width +
                height * height
            );

        const closureRatio =
            closingDistance / Math.max(diagonal, 1);

        const aspect =
            width / height;

        /*
         * Calculate path length.
         */
        let length = 0;

        for (let i = 1; i < points.length; i++) {

            length += Math.sqrt(
                Math.pow(points[i].x - points[i - 1].x, 2) +
                Math.pow(points[i].y - points[i - 1].y, 2)
            );
        }

        /*
         * Straightness.
         *
         * If path length is close to the direct distance
         * between first and last point, it is probably a line.
         */
        const directDistance =
            Math.sqrt(
                Math.pow(last.x - first.x, 2) +
                Math.pow(last.y - first.y, 2)
            );

        const straightness =
            directDistance / Math.max(length, 1);

        if (
            straightness > 0.85 &&
            closureRatio > 0.25
        ) {
            return "Line";
        }

        /*
         * Calculate center.
         */
        const centerX =
            points.reduce(
                (sum, p) => sum + p.x,
                0
            ) / points.length;

        const centerY =
            points.reduce(
                (sum, p) => sum + p.y,
                0
            ) / points.length;

        /*
         * Radial distances from center.
         */
        const radii = points.map(p =>
            Math.sqrt(
                Math.pow(p.x - centerX, 2) +
                Math.pow(p.y - centerY, 2)
            )
        );

        const meanRadius =
            radii.reduce(
                (sum, r) => sum + r,
                0
            ) / radii.length;

        const radiusVariance =
            radii.reduce(
                (sum, r) =>
                    sum + Math.pow(r - meanRadius, 2),
                0
            ) / radii.length;

        const radiusStd =
            Math.sqrt(radiusVariance);

        const circleVariation =
            radiusStd /
            Math.max(meanRadius, 1);

        /*
         * Closed and fairly circular.
         */
        if (
            closureRatio < 0.30 &&
            circleVariation < 0.35 &&
            aspect > 0.65 &&
            aspect < 1.50
        ) {
            return "Circle";
        }

        /*
         * Estimate corners by direction changes.
         */
        let directionChanges = 0;

        let previousAngle = null;

        for (let i = 2; i < points.length; i++) {

            const dx1 =
                points[i - 1].x -
                points[i - 2].x;

            const dy1 =
                points[i - 1].y -
                points[i - 2].y;

            const dx2 =
                points[i].x -
                points[i - 1].x;

            const dy2 =
                points[i].y -
                points[i - 1].y;

            const angle1 =
                Math.atan2(dy1, dx1);

            const angle2 =
                Math.atan2(dy2, dx2);

            let diff =
                Math.abs(angle2 - angle1);

            if (diff > Math.PI)
                diff = 2 * Math.PI - diff;

            if (diff > 0.65)
                directionChanges++;
        }

        /*
         * Triangle:
         * relatively closed + several strong direction changes,
         * but fewer than a zigzag.
         */
        if (
            closureRatio < 0.35 &&
            directionChanges >= 2 &&
            directionChanges <= 5
        ) {
            return "Triangle";
        }

        /*
         * Square:
         * closed, approximately balanced width/height,
         * and noticeable direction changes.
         */
        if (
            closureRatio < 0.35 &&
            aspect > 0.65 &&
            aspect < 1.50 &&
            directionChanges >= 4 &&
            directionChanges <= 12
        ) {
            return "Square";
        }

        /*
         * Zigzag:
         * many direction changes and not closed.
         */
        if (
            directionChanges >= 4 &&
            closureRatio > 0.25
        ) {
            return "Zigzag";
        }

        /*
         * Final fallback.
         */
        if (closureRatio < 0.35)
            return "Circle";

        return "Zigzag";
    }

    // =========================================================
    // KEY NORMALIZATION
    // =========================================================

    function normalizeKey(key) {

        if (!key)
            return "";

        if (key.length === 1)
            return key.toUpperCase();

        return key;
    }

    // =========================================================
    // SETUP RECORDING
    // =========================================================

    function start(elementId, selectedPattern) {

        stop();

        area =
            document.getElementById(elementId);

        if (!area)
            throw new Error(
                "Behavioral recording area not found."
            );

        patternType =
            selectedPattern;

        reset();

        recording = true;

        area.addEventListener(
            "mousedown",
            mouseDownHandler,
            true
        );

        area.addEventListener(
            "contextmenu",
            contextMenuHandler,
            true
        );

        area.addEventListener(
            "pointerdown",
            pointerDownHandler,
            true
        );

        area.addEventListener(
            "pointermove",
            pointerMoveHandler,
            true
        );

        area.addEventListener(
            "pointerup",
            pointerUpHandler,
            true
        );

        document.addEventListener(
            "keydown",
            shortcutKeyHandler,
            true
        );

        document.addEventListener(
            "keydown",
            sequenceKeyHandler,
            true
        );

        return true;
    }

    // =========================================================
    // NORMAL STOP
    // =========================================================

    function stop() {

        clearTimers();

        if (area) {

            area.removeEventListener(
                "mousedown",
                mouseDownHandler,
                true
            );

            area.removeEventListener(
                "contextmenu",
                contextMenuHandler,
                true
            );

            area.removeEventListener(
                "pointerdown",
                pointerDownHandler,
                true
            );

            area.removeEventListener(
                "pointermove",
                pointerMoveHandler,
                true
            );

            area.removeEventListener(
                "pointerup",
                pointerUpHandler,
                true
            );
        }

        document.removeEventListener(
            "keydown",
            shortcutKeyHandler,
            true
        );

        document.removeEventListener(
            "keydown",
            sequenceKeyHandler,
            true
        );

        const result = {
            patternType: patternType,

            clickType:
                clicks.length > 0
                    ? clicks[0].button
                    : "",

            clickCount:
                clicks.length,

            clicks:
                clicks.slice(),

            shapeType:
                path.length >= 5
                    ? classifyShape(path)
                    : "",

            path:
                path.slice(),

            shortcut:
            { ...shortcut },

            keySequence:
                keySequence.slice()
        };

        recording = false;

        area = null;

        patternType = '';

        reset();

        return result;
    }

    // =========================================================
    // AUTOMATIC VERIFICATION
    // =========================================================

    function captureAutomatically(
        elementId,
        selectedPattern
    ) {

        stop();

        area =
            document.getElementById(elementId);

        if (!area)
            throw new Error(
                "Behavioral verification area not found."
            );

        patternType =
            selectedPattern;

        reset();

        recording = true;

        attachAutomaticHandlers();

        return new Promise(resolve => {

            autoResolve = resolve;

        });
    }

    function attachAutomaticHandlers() {

        if (!area)
            return;

        area.addEventListener(
            "mousedown",
            mouseDownHandler,
            true
        );

        area.addEventListener(
            "contextmenu",
            contextMenuHandler,
            true
        );

        area.addEventListener(
            "pointerdown",
            pointerDownHandler,
            true
        );

        area.addEventListener(
            "pointermove",
            pointerMoveHandler,
            true
        );

        area.addEventListener(
            "pointerup",
            pointerUpHandler,
            true
        );

        document.addEventListener(
            "keydown",
            shortcutKeyHandler,
            true
        );

        document.addEventListener(
            "keydown",
            sequenceKeyHandler,
            true
        );
    }

    function finishAutomaticCapture(extra = {}) {

        if (!autoResolve)
            return;

        const result = {
            patternType: patternType,

            clickType:
                clicks.length > 0
                    ? clicks[0].button
                    : "",

            clickCount:
                clicks.length,

            clicks:
                clicks.slice(),

            shapeType:
                extra.shapeType ||
                (
                    path.length >= 5
                        ? classifyShape(path)
                        : ""
                ),

            path:
                path.slice(),

            shortcut:
            { ...shortcut },

            keySequence:
                keySequence.slice()
        };

        const resolve =
            autoResolve;

        autoResolve = null;

        cleanupAutomaticHandlers();

        resolve(result);
    }

    function cleanupAutomaticHandlers() {

        clearTimers();

        if (area) {

            area.removeEventListener(
                "mousedown",
                mouseDownHandler,
                true
            );

            area.removeEventListener(
                "contextmenu",
                contextMenuHandler,
                true
            );

            area.removeEventListener(
                "pointerdown",
                pointerDownHandler,
                true
            );

            area.removeEventListener(
                "pointermove",
                pointerMoveHandler,
                true
            );

            area.removeEventListener(
                "pointerup",
                pointerUpHandler,
                true
            );
        }

        document.removeEventListener(
            "keydown",
            shortcutKeyHandler,
            true
        );

        document.removeEventListener(
            "keydown",
            sequenceKeyHandler,
            true
        );

        recording = false;

        area = null;

        patternType = '';

        reset();
    }

    function stopAutomatic() {

        if (autoResolve) {

            const resolve =
                autoResolve;

            autoResolve = null;

            cleanupAutomaticHandlers();

            resolve(null);

            return;
        }

        cleanupAutomaticHandlers();
    }

    return {

        start,

        stop,

        captureAutomatically,

        stopAutomatic

    };

})();