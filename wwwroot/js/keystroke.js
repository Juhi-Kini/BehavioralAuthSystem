window.keystrokeCapture = {
    instances: {},
    globalSampler: null,

    attach: function (elementId, dotNetRef) {
        if (this.instances[elementId]) {
            this.detach(elementId);
        }

        const el = document.getElementById(elementId);

        if (!el) {
            console.error(
                "Keystroke capture input not found:",
                elementId
            );
            return false;
        }

        let keyState = {};
        let keyDownTimes = [];
        let keyUpTimes = [];
        let keyHoldTimes = [];
        let keyLatencies = [];
        let startTime = 0;

        function resetTracker() {
            keyState = {};
            keyDownTimes = [];
            keyUpTimes = [];
            keyHoldTimes = [];
            keyLatencies = [];
            startTime = 0;
        }

        const handleKeyDown = function (e) {
            // Ignore repeated keydown events.
            if (e.repeat) return;

            // Ignore modifier and navigation keys.
            // 'Enter' is the submit key, not part of the password rhythm.
            if ([
                'Shift', 'Control', 'Alt', 'Meta',
                'CapsLock', 'Tab', 'Escape', 'Enter'
            ].includes(e.key)) return;

            const now = performance.now();

            if (keyDownTimes.length === 0) {
                startTime = now;
            }

            if (!keyState[e.code]) {
                keyState[e.code] = now;

                if (keyDownTimes.length > 0) {
                    const prevKeyDown = keyDownTimes[keyDownTimes.length - 1];
                    const latency = now - prevKeyDown;

                    if (latency > 0) {
                        keyLatencies.push(latency);
                    }
                }

                keyDownTimes.push(now);
            }
        };
        const handleKeyUp = function (e) {
            if (['Shift', 'Control', 'Alt', 'Meta', 'CapsLock', 'Tab', 'Escape', 'Enter'].includes(e.key)) return;

            const now = performance.now();
            if (keyState[e.code]) {
                const pressTime = keyState[e.code];
                const hold = now - pressTime;

                // Only record positive, realistic hold durations
                if (hold > 0 && hold < 5000) {
                    keyHoldTimes.push(hold);
                    keyUpTimes.push(now);
                }

                delete keyState[e.code];

                // Trigger waveform update if renderer exists
                if (window.waveformRenderer) {
                    window.waveformRenderer.update(elementId + '-waveform', keyHoldTimes, keyLatencies);
                }

                if (dotNetRef) {
                    try {
                        const totalDuration = now - startTime;
                        dotNetRef.invokeMethodAsync('OnKeystrokeCaptured', {
                            holdTimes: keyHoldTimes,
                            latencies: keyLatencies,
                            totalDuration: totalDuration,
                            keyCount: keyHoldTimes.length
                        });
                    } catch (e) {
                        // Circuit or ref might be disposed
                    }
                }
            }
        };

        const handleInput = function (e) {
            if (el.value === '') {
                resetTracker();
                if (window.waveformRenderer) {
                    window.waveformRenderer.clear(elementId + '-waveform');
                }
            }
        };

        el.addEventListener('keydown', handleKeyDown);
        el.addEventListener('keyup', handleKeyUp);
        el.addEventListener('input', handleInput);

        this.instances[elementId] = {
            element: el,
            reset: resetTracker,
            getData: function () {
                const totalDuration = keyDownTimes.length > 0 ? (performance.now() - startTime) : 0;
                // N completed keys always have N-1 latencies. A trailing keydown whose
                // keyup was never seen (e.g. the submit key) must not add an extra latency.
                const maxLatencies = Math.max(0, keyHoldTimes.length - 1);
                return {
                    holdTimes: [...keyHoldTimes],
                    latencies: keyLatencies.slice(0, maxLatencies),
                    totalDuration: totalDuration,
                    keyCount: keyHoldTimes.length
                };
            },
            cleanup: function () {
                el.removeEventListener('keydown', handleKeyDown);
                el.removeEventListener('keyup', handleKeyUp);
                el.removeEventListener('input', handleInput);
            }
        };
        return true;
    },

    reset: function (elementId) {
        if (this.instances[elementId]) {
            this.instances[elementId].reset();
            if (window.waveformRenderer) {
                window.waveformRenderer.clear(elementId + '-waveform');
            }
        }
    },

    getTimingData: function (elementId) {
        if (this.instances[elementId]) {
            return this.instances[elementId].getData();
        }
        return { holdTimes: [], latencies: [], totalDuration: 0, keyCount: 0 };
    },

    detach: function (elementId) {
        if (this.instances[elementId]) {
            this.instances[elementId].cleanup();
            delete this.instances[elementId];
        }
    },

    // Continuous background re-auth sampler with safe disposal and password protection
    attachGlobalSampler: function (dotNetRef) {
        if (this.globalSampler) {
            this.detachGlobalSampler();
        }

        let globalHold = [];
        let globalLat = [];
        let lastKeyDown = 0;
        let activeState = {};

        const onKeyDown = function (e) {
            if (e.repeat) return;
            // Privacy Protection: NEVER sample typing from password fields!
            if (e.target && (e.target.type === 'password' || e.target.getAttribute('data-no-biometrics') === 'true')) {
                return;
            }
            if (['Shift', 'Control', 'Alt', 'Meta', 'CapsLock', 'Tab'].includes(e.key)) return;

            const now = performance.now();
            if (lastKeyDown > 0) {
                const lat = now - lastKeyDown;
                if (lat > 10 && lat < 5000) {
                    globalLat.push(lat);
                }
            }
            lastKeyDown = now;
            activeState[e.code] = now;
        };

        const onKeyUp = function (e) {
            if (e.target && (e.target.type === 'password' || e.target.getAttribute('data-no-biometrics') === 'true')) {
                return;
            }
            if (['Shift', 'Control', 'Alt', 'Meta', 'CapsLock', 'Tab'].includes(e.key)) return;

            const now = performance.now();
            if (activeState[e.code]) {
                const hold = now - activeState[e.code];
                if (hold > 10 && hold < 3000) {
                    globalHold.push(hold);
                }
                delete activeState[e.code];

                // Every 15 clean keystrokes, send background sample for continuous re-auth evaluation
                if (globalHold.length >= 15 && dotNetRef) {
                    try {
                        dotNetRef.invokeMethodAsync('OnContinuousSampleCaptured', {
                            holdTimes: [...globalHold],
                            latencies: [...globalLat],
                            totalDuration: 0,
                            keyCount: globalHold.length
                        });
                    } catch (err) {
                        // Layout or connection may be closed
                    }
                    globalHold = [];
                    globalLat = [];
                }
            }
        };

        document.addEventListener('keydown', onKeyDown);
        document.addEventListener('keyup', onKeyUp);

        this.globalSampler = {
            onKeyDown: onKeyDown,
            onKeyUp: onKeyUp,
            cleanup: function () {
                document.removeEventListener('keydown', onKeyDown);
                document.removeEventListener('keyup', onKeyUp);
            }
        };
    },

    detachGlobalSampler: function () {
        if (this.globalSampler) {
            this.globalSampler.cleanup();
            this.globalSampler = null;
        }
    }
};

window.authInterop = {
    login: async function (username, password, keystrokeSample) {
        try {
            const response = await fetch('/api/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    username: username,
                    password: password,
                    keystrokeSample: keystrokeSample
                })
            });

            const responseText = await response.text();

            let data;

            try {
                data = JSON.parse(responseText);
            }
            catch {
                data = {
                    success: false,
                    message: responseText || `Server returned HTTP ${response.status}.`
                };
            }

            return {
                status: response.status,
                ok: response.ok,
                data: data
            };
        } catch (err) {
            return {
                status: 500,
                ok: false,
                data: {
                    success: false,
                    message: 'Network error communicating with authentication service: ' + err.message
                }
            };
        }
    },

    logout: async function () {
        try {
            await fetch('/api/auth/sign-out', { method: 'POST' });
        } catch (e) { }
        window.location.href = '/login';
    }
};