// Вспомогательный JS AI2P (точечный interop, ТЗ гл. 5): ресайз фреймов мышкой,
// работа с textarea MD-редактора (вставка/обёртка текста, вставка картинок из буфера),
// копирование в буфер обмена.
window.ai2p = (function () {
    'use strict';

    // --- ресайз фреймов (ТЗ гл. 11): перетаскивание разделителя меняет ширину панели ---
    function initSplitter(splitterId, targetId, storageKey) {
        const splitter = document.getElementById(splitterId);
        const target = document.getElementById(targetId);
        if (!splitter || !target || splitter.dataset.ai2pInit) return;
        splitter.dataset.ai2pInit = '1';

        const saved = localStorage.getItem(storageKey);
        if (saved) target.style.flexBasis = saved + 'px';

        splitter.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            splitter.setPointerCapture(e.pointerId);
            const startX = e.clientX;
            const startWidth = target.getBoundingClientRect().width;

            function onMove(ev) {
                const width = Math.min(Math.max(startWidth + ev.clientX - startX, 120), 800);
                target.style.flexBasis = width + 'px';
            }
            function onUp(ev) {
                splitter.releasePointerCapture(ev.pointerId);
                splitter.removeEventListener('pointermove', onMove);
                splitter.removeEventListener('pointerup', onUp);
                localStorage.setItem(storageKey, Math.round(target.getBoundingClientRect().width));
            }
            splitter.addEventListener('pointermove', onMove);
            splitter.addEventListener('pointerup', onUp);
        });
    }

    // --- делители фреймов рабочей области (todo22): перетаскивание меняет CSS-grid,
    // на отпускании позиция уходит в .NET (OnFrameRatioChanged) и сохраняется в лейауте ---
    function initFrameSplitters(containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.querySelectorAll('.ai2p-frame-splitter:not([data-ai2p-init])').forEach(function (splitter) {
            splitter.dataset.ai2pInit = '1';
            splitter.addEventListener('pointerdown', function (e) {
                e.preventDefault();
                splitter.setPointerCapture(e.pointerId);
                const axis = splitter.dataset.axis; // v — вертикальный делитель, h — горизонтальный

                function percentOf(ev) {
                    const rect = container.getBoundingClientRect();
                    const pct = axis === 'h'
                        ? (ev.clientY - rect.top) / rect.height * 100
                        : (ev.clientX - rect.left) / rect.width * 100;
                    return Math.min(Math.max(pct, 10), 90);
                }
                function apply(pct) {
                    if (axis === 'h') container.style.gridTemplateRows = pct + '% 6px 1fr';
                    else container.style.gridTemplateColumns = pct + '% 6px 1fr';
                }
                function onMove(ev) { apply(percentOf(ev)); }
                function onUp(ev) {
                    splitter.releasePointerCapture(ev.pointerId);
                    splitter.removeEventListener('pointermove', onMove);
                    splitter.removeEventListener('pointerup', onUp);
                    dotnetRef.invokeMethodAsync('OnFrameRatioChanged', axis, Math.round(percentOf(ev)));
                }
                splitter.addEventListener('pointermove', onMove);
                splitter.addEventListener('pointerup', onUp);
            });
        });
    }

    // --- drag-n-drop закладок между фреймами (todo22, багфикс todo23) ---
    // Целиком в JS: Blazor-обработчики drag-событий не заполняют dataTransfer, а Firefox
    // без setData в dragstart не начинает перетаскивание (курсор «запрещено»). Делегированные
    // слушатели на контейнере переживают ререндеры Blazor.
    function initTabDnd(containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container || container.dataset.ai2pDnd) return;
        container.dataset.ai2pDnd = '1';

        container.addEventListener('dragstart', function (e) {
            const tab = e.target.closest('.ai2p-tab[data-frame]');
            if (!tab) return;
            e.dataTransfer.setData('text/plain', tab.dataset.frame + ':' + tab.dataset.tab);
            e.dataTransfer.effectAllowed = 'move';
        });
        container.addEventListener('dragover', function (e) {
            if (e.target.closest('[data-frame-drop]')) {
                e.preventDefault(); // разрешить сброс (иначе курсор «запрещено»)
                e.dataTransfer.dropEffect = 'move';
            }
        });
        container.addEventListener('drop', function (e) {
            const frame = e.target.closest('[data-frame-drop]');
            if (!frame) return;
            e.preventDefault();
            const parts = (e.dataTransfer.getData('text/plain') || '').split(':');
            if (parts.length !== 2) return;
            const from = parseInt(parts[0], 10);
            const tab = parseInt(parts[1], 10);
            if (isNaN(from) || isNaN(tab)) return;
            dotnetRef.invokeMethodAsync('OnTabDrop', from, tab, parseInt(frame.dataset.frameDrop, 10));
        });
    }

    // --- ЛИСТАЕМАЯ ПОЛОСА: общий движок (T-163 — закладки, T-194 — тулбары) ---
    // Содержимое стоит в ОДНУ строку: не поместившееся не переносится вниз и не сжимается,
    // а прокручивается. Стрелки показываются только при переполнении (класс …-scrolls на
    // полосе) и гаснут на краях (ai2p-arrow-off). Мерить ширины умеет только браузер,
    // поэтому состояние стрелок — здесь, а не в .NET; Blazor классы полосы не переписывает
    // (они в разметке постоянные), и после каждого рендера init… зовётся заново.
    const TabScrollStep = 0.8;  // за нажатие — 80% видимой ширины полосы

    // общее состояние полосы: есть ли переполнение и не доехали ли до края
    function stripUpdate(bar, strip, scrollsClass, prev, next) {
        if (!strip) return;
        const overflow = strip.scrollWidth - strip.clientWidth > 1;
        bar.classList.toggle(scrollsClass, overflow);
        const left = strip.scrollLeft;
        if (prev) prev.classList.toggle('ai2p-arrow-off', !overflow || left <= 1);
        if (next) next.classList.toggle('ai2p-arrow-off',
            !overflow || left + strip.clientWidth >= strip.scrollWidth - 1);
    }

    // слушатели полосы ставятся ОДИН раз на элемент: прокрутка (пальцем на телефоне и
    // кнопками), колесо мыши и изменение размеров — всё это меняет состояние стрелок
    function stripWire(strip, update) {
        if (strip.dataset.ai2pStrip) return false;
        strip.dataset.ai2pStrip = '1';
        strip.addEventListener('scroll', update, { passive: true });
        // колесо мыши над полосой листает её: вертикально прокручивать тут нечего
        strip.addEventListener('wheel', function (e) {
            const delta = e.deltaY || e.deltaX;
            if (!delta || strip.scrollWidth - strip.clientWidth <= 1) return;
            e.preventDefault();
            strip.scrollLeft += delta;
        }, { passive: false });
        if ('ResizeObserver' in window) {
            // окно повернули, фрейм сузили делителем — переполнение перемерить
            new ResizeObserver(update).observe(strip);
        }
        return true;
    }

    // листнуть полосу на 80% её ширины; dir: -1 — влево, 1 — вправо
    function stripScroll(strip, dir) {
        if (!strip) return;
        strip.scrollLeft += dir * Math.max(strip.clientWidth * TabScrollStep, 80);
    }

    function tabScrollUpdate(bar) {
        stripUpdate(bar, bar.querySelector('.ai2p-tabbar-strip'), 'ai2p-tabbar-scrolls',
            bar.querySelector('[data-tab-scroll="prev"]'),
            bar.querySelector('[data-tab-scroll="next"]'));
    }

    // --- ТУЛБАРЫ, КОТОРЫЕ НЕ ВЛЕЗЛИ (T-194, работа на телефоне) ---
    // На экране телефона полоса меню и тулбар списка задач шире окна. Раньше это кончалось
    // двумя бедами: часть кнопок оказывалась за краем (.ai2p-shell с overflow: hidden — до
    // них было не добраться вовсе), а тулбар представления утаскивал за собой в бок ВСЁ
    // содержимое фрейма — доску вместе с собой. Теперь каждая такая полоса листается САМА,
    // теми же кнопками «‹ ›», что и закладки фреймов.
    function initStrips(root) {
        const box = root ? document.getElementById(root) : document;
        if (!box) return;
        box.querySelectorAll('.ai2p-strip').forEach(function (bar) {
            const strip = bar.querySelector('.ai2p-strip-inner');
            if (!strip) return;
            const update = function () {
                stripUpdate(bar, strip, 'ai2p-strip-scrolls',
                    bar.querySelector('[data-strip-scroll="prev"]'),
                    bar.querySelector('[data-strip-scroll="next"]'));
            };
            stripWire(strip, update);
            update();
        });
    }

    // листание тулбара кнопкой: ключ полосы — data-strip-inner (у каждой свой)
    function scrollStrip(key, dir) {
        const strip = document.querySelector('.ai2p-strip-inner[data-strip-inner="' + key + '"]');
        if (!strip) return;
        stripScroll(strip, dir);
        const bar = strip.closest('.ai2p-strip');
        if (bar) {
            stripUpdate(bar, strip, 'ai2p-strip-scrolls',
                bar.querySelector('[data-strip-scroll="prev"]'),
                bar.querySelector('[data-strip-scroll="next"]'));
        }
    }

    // активная закладка обязана быть на виду: её могли открыть из эксплорера или меню,
    // когда полоса отлистана в другой конец. ВАЖНО: только когда активная СМЕНИЛАСЬ —
    // initTabScroll зовётся после каждого рендера, и безусловная доводка отматывала бы
    // полосу назад сразу после того, как её отлистали кнопкой (поймано живой проверкой)
    function tabScrollToActive(strip) {
        const active = strip.querySelector('.ai2p-tab.active');
        if (!active) return;
        const key = (active.dataset.tab || '') + '|' + (active.textContent || '').trim();
        if (strip.dataset.ai2pActive === key) return;
        strip.dataset.ai2pActive = key;
        const left = active.offsetLeft;
        const right = left + active.offsetWidth;
        if (left < strip.scrollLeft) strip.scrollLeft = Math.max(left - 8, 0);
        else if (right > strip.scrollLeft + strip.clientWidth) {
            strip.scrollLeft = right - strip.clientWidth + 8;
        }
    }

    function initTabScroll(containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.querySelectorAll('.ai2p-tabbar').forEach(function (bar) {
            const strip = bar.querySelector('.ai2p-tabbar-strip');
            if (!strip) return;
            stripWire(strip, function () { tabScrollUpdate(bar); });
            tabScrollToActive(strip);
            tabScrollUpdate(bar);
        });
    }

    function scrollTabs(frameIndex, dir) {
        const strip = document.querySelector('.ai2p-tabbar-strip[data-tab-strip="' + frameIndex + '"]');
        if (!strip) return;
        stripScroll(strip, dir); // scroll-behavior: smooth — плавность делает CSS
        const bar = strip.closest('.ai2p-tabbar');
        if (bar) tabScrollUpdate(bar);
    }

    // --- перетаскивание ПАЛЬЦЕМ (T-193, мобильная версия) ---
    // На телефоне (Firefox/Chrome на Android, Safari на iOS) браузер начинает НАТИВНОЕ
    // перетаскивание элемента с draggable="true" от обычного движения пальца: вместо
    // прокрутки доски или дерева из-под пальца уезжает карточка. Лечится в три хода:
    //   1) на время касания нативное перетаскивание выключается (draggable=false) —
    //      палец снова прокручивает колонку и список;
    //   2) пока палец на экране, начатое браузером перетаскивание отменяется в dragstart.
    //      Одного снятия атрибута мало: Blazor перерисовывает строки прямо во время
    //      прокрутки (ленивые превью), и draggable="true" из разметки возвращается сам
    //      собой посреди касания — второй заход T-193;
    //   3) перенос делается своим жестом: подержать палец TouchHoldMs, не сдвигая его
    //      дальше TouchMoveTol, — только тогда строка «прилипает» к пальцу (как в
    //      Trello). Поехал раньше — это прокрутка, и жест не начинается вовсе.
    // Нативное перетаскивание мышкой всё это не трогает: touch-события мышь не шлёт,
    // а draggable возвращается на место сразу по окончании касания.
    //
    // T-4-S1: у правила может быть РУЧКА (handle) — узкая зона строки, за которую перенос
    // начинается СРАЗУ, без удержания (rule.hold = 0). Удержание — жест «на ощупь»: на
    // настоящем телефоне его легко не поймать (палец дрожит, браузер успевает начать
    // выделение текста или своё контекстное меню), поэтому у дерева задач есть ещё и
    // видимая ручка, и она же — подсказка «строку можно двигать».
    //
    // Движок общий: правило описывает, что тащим (source), за что берём (handle, можно не
    // указывать), куда бросаем (target), чем подсвечиваем цель (over) и что делать при
    // сбросе (drop). Им пользуются доска (заголовки колонок) и дерево задач.
    const TouchHoldMs = 400;   // сколько держать палец, чтобы начался перенос
    const TouchMoveTol = 16;   // сдвиг до этого — обычная прокрутка, переноса нет, px
    const TouchEdge = 60;      // полоса автопрокрутки у края области, px
    const TouchStep = 14;      // на сколько прокручивать за такт, px

    // ближайшие прокручиваемые предки элемента — по вертикали и по горизонтали
    // (колонка доски и сама доска: у них разные оси)
    function touchScrollers(node) {
        const res = { v: null, h: null };
        let el = node;
        while (el && el !== document.body && (res.v === null || res.h === null)) {
            const style = getComputedStyle(el);
            if (!res.v && /(auto|scroll|overlay)/.test(style.overflowY) &&
                el.scrollHeight > el.clientHeight + 2) res.v = el;
            if (!res.h && /(auto|scroll|overlay)/.test(style.overflowX) &&
                el.scrollWidth > el.clientWidth + 2) res.h = el;
            el = el.parentElement;
        }
        return res;
    }

    function initTouchDnd(container, rules) {
        if (!container || container.dataset.ai2pTouchDnd) return;
        container.dataset.ai2pTouchDnd = '1';

        let rule = null;      // правило, по которому идёт текущий жест
        let src = null;       // элемент-источник под пальцем
        let ghost = null;     // копия источника, летящая за пальцем
        let over = null;      // подсвеченная цель
        let holdTimer = null;
        let scrollTimer = null;
        let startX = 0, startY = 0, x = 0, y = 0, grabX = 0, grabY = 0;
        let disabled = [];    // элементы, у которых на время касания снят draggable
        let touching = false; // палец на экране: нативное перетаскивание запрещено

        function noNativeDrag(node) {
            let el = node;
            while (el && el !== document.body) {
                if (el.getAttribute && el.getAttribute('draggable') === 'true') {
                    el.setAttribute('draggable', 'false');
                    disabled.push(el);
                }
                el = el.parentElement;
            }
        }

        function restoreNativeDrag() {
            disabled.forEach(function (el) { el.setAttribute('draggable', 'true'); });
            disabled = [];
        }

        function setOver(target) {
            if (over === target) return;
            if (over) over.classList.remove(rule.over);
            over = target;
            if (over) over.classList.add(rule.over);
        }

        function targetAt() {
            // ghost не мешает: у него pointer-events: none
            const el = document.elementFromPoint(x, y);
            if (!el) return null;
            const target = el.closest(rule.target);
            return target && container.contains(target) ? target : null;
        }

        function autoScrollStep() {
            // источник убрали из документа прямо во время переноса (закрыли закладку) —
            // тогда touchend уже не придёт, а таймер крутил бы список сам собой
            if (!src || !container.isConnected) { reset(); return; }
            const under = document.elementFromPoint(x, y);
            if (!under) return;
            const boxes = touchScrollers(under);
            if (boxes.v) {
                const r = boxes.v.getBoundingClientRect();
                // верхний край считается ПОД липкой зоной сброса (полоса «в корень» дерева):
                // иначе, подводя к ней задачу, попадаешь в полосу автопрокрутки, список
                // уезжает из-под пальца и сбросить в корень нельзя — та же грабля, что была
                // у мыши в T-176
                const sticky = rule.sticky ? container.querySelector(rule.sticky) : null;
                const top = sticky ? Math.max(r.top, sticky.getBoundingClientRect().bottom) : r.top;
                if (y > top && y < top + TouchEdge) boxes.v.scrollTop -= TouchStep;
                else if (y > r.bottom - TouchEdge) boxes.v.scrollTop += TouchStep;
            }
            if (boxes.h) {
                const r = boxes.h.getBoundingClientRect();
                if (x < r.left + TouchEdge) boxes.h.scrollLeft -= TouchStep;
                else if (x > r.right - TouchEdge) boxes.h.scrollLeft += TouchStep;
            }
        }

        function begin() {
            holdTimer = null;
            // удержание пальца браузер на телефоне понимает по-своему: Chrome на Android
            // выделяет слово и показывает маркеры выделения, Safari на iOS — своё меню, и
            // дальше палец ведёт МАРКЕР, а не строку. Выделение снимаем, а нативное
            // перетаскивание запрещаем ещё раз: Blazor мог перерисовать строку и вернуть ей
            // draggable="true" за те 400 мс, что палец стоял на месте (T-4-S1)
            const sel = window.getSelection && window.getSelection();
            if (sel && sel.removeAllRanges) sel.removeAllRanges();
            noNativeDrag(src);
            const box = src.getBoundingClientRect();
            grabX = x - box.left;
            grabY = y - box.top;
            ghost = src.cloneNode(true);
            ghost.classList.add('ai2p-touch-ghost');
            ghost.style.width = box.width + 'px';
            // копия карточки не должна попасть под наблюдателя ленивых превью: он позвал бы
            // .NET за описанием ещё раз — уже для летящей за пальцем копии
            ghost.querySelectorAll('.ai2p-lazy-md').forEach(function (el) { el.dataset.observed = '1'; });
            document.body.appendChild(ghost);
            moveGhost();
            src.classList.add('ai2p-touch-src');
            if (rule.begin) rule.begin(container);
            if (navigator.vibrate) navigator.vibrate(20); // «взял» — как в мобильных списках
            scrollTimer = setInterval(autoScrollStep, 60);
        }

        function moveGhost() {
            ghost.style.left = (x - grabX) + 'px';
            ghost.style.top = (y - grabY) + 'px';
        }

        // ВАЖНО: draggable здесь НЕ возвращается. Жест обрывается и посреди касания (палец
        // поехал — это прокрутка), а вернуть в этот момент draggable="true" значило бы
        // отдать браузеру ровно то движение пальца, из-за которого всё и чинилось.
        // Возврат — в конце касания (touchend/touchcancel)
        function reset() {
            if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
            if (scrollTimer) { clearInterval(scrollTimer); scrollTimer = null; }
            if (ghost) { ghost.remove(); ghost = null; }
            if (over) { over.classList.remove(rule.over); over = null; }
            if (src) src.classList.remove('ai2p-touch-src');
            if (rule && rule.end) rule.end(container);
            src = null;
            rule = null;
        }

        // палец на экране — нативного перетаскивания быть не должно вовсе, даже если
        // Blazor успел перерисовать строку и вернуть ей draggable="true" (перехват на
        // фазе перехвата: до обработчиков самой доски и дерева)
        container.addEventListener('dragstart', function (e) {
            if (touching) e.preventDefault();
        }, true);

        // длинное нажатие на телефоне вызывает контекстное меню браузера («открыть в новой
        // вкладке», «копировать»), и жест переноса умирает вместе с касанием. Пока палец на
        // экране, меню не показываем (T-4-S1)
        container.addEventListener('contextmenu', function (e) {
            if (touching) e.preventDefault();
        });

        container.addEventListener('touchstart', function (e) {
            reset();     // остаток прошлого касания (второй палец, отменённый жест)
            unbindSrc(); // и его «личные» обработчики, если касание кончилось не туда
            touching = true;
            noNativeDrag(e.target);
            if (e.touches.length > 1 || !e.target.closest) return;
            // правила с РУЧКОЙ идут первыми: касание ручки — намеренное, ждать удержания
            // незачем (rule.hold === 0), и прокрутке списка это не мешает — ручка узкая
            const ordered = rules.filter(function (r) { return r.handle; })
                                 .concat(rules.filter(function (r) { return !r.handle; }));
            for (let i = 0; i < ordered.length; i++) {
                if (ordered[i].handle && !e.target.closest(ordered[i].handle)) continue;
                const el = e.target.closest(ordered[i].source);
                if (el && container.contains(el)) { rule = ordered[i]; src = el; break; }
            }
            if (!src) return;
            bindSrc(src); // жест переживёт перерисовку списка (см. ниже)
            const t = e.touches[0];
            startX = x = t.clientX;
            startY = y = t.clientY;
            holdTimer = setTimeout(begin, rule.hold === undefined ? TouchHoldMs : rule.hold);
        }, { passive: true });

        // Обработчики висят и на контейнере, и на САМОЙ взятой строке (T-4-S1). Причина:
        // касание браузер доставляет тому узлу, на котором оно началось, — а Blazor во время
        // жеста список перерисовывает, и старая строка уходит из документа. Всплывать
        // событию тогда некуда (у отсоединённого узла нет предков-контейнера), и жест
        // пропадал молча: палец ведёт копию, отпускает — и ничего не происходит. Слушатель
        // на самой строке доезжает всегда, потому что событие идёт по её собственному
        // поддереву. Флаг у события — чтобы обработать его РОВНО один раз.
        function once(e) {
            if (e.__ai2pTouch) return false;
            e.__ai2pTouch = true;
            return true;
        }

        let bound = null; // строка, на которой висят «личные» обработчики жеста

        function bindSrc(node) {
            unbindSrc();
            bound = node;
            node.addEventListener('touchmove', onMove, { passive: false });
            node.addEventListener('touchend', onEnd, { passive: false });
            node.addEventListener('touchcancel', onCancel, { passive: false });
        }

        function unbindSrc() {
            if (!bound) return;
            bound.removeEventListener('touchmove', onMove);
            bound.removeEventListener('touchend', onEnd);
            bound.removeEventListener('touchcancel', onCancel);
            bound = null;
        }

        function onMove(e) {
            if (!src || !once(e)) return;
            if (e.touches.length > 1) { reset(); return; }
            const t = e.touches[0];
            x = t.clientX;
            y = t.clientY;
            if (!ghost) {
                // палец поехал раньше, чем сработало удержание, — это прокрутка. Взятое за
                // ручку так не отменяется: там переносить начали намеренно, а копия за
                // пальцем появится ближайшим тактом таймера (T-4-S1)
                if (rule.hold !== 0 &&
                    (Math.abs(x - startX) > TouchMoveTol || Math.abs(y - startY) > TouchMoveTol)) reset();
                return;
            }
            e.preventDefault();
            moveGhost();
            setOver(targetAt());
        }

        function onEnd(e) {
            if (!once(e)) return;
            const dropped = src && ghost && over ? { rule: rule, src: src, target: over, x: x } : null;
            // после переноса «клика» по карточке быть не должно: иначе поверх доски
            // сразу откроется карточка задачи
            if (ghost && e.cancelable) e.preventDefault();
            reset();
            touching = false;
            unbindSrc();
            restoreNativeDrag();
            if (dropped) dropped.rule.drop(dropped.src, dropped.target, dropped.x);
        }

        function onCancel(e) {
            if (!once(e)) return;
            reset();
            touching = false;
            unbindSrc();
            restoreNativeDrag();
        }

        // слушатель НЕ passive: пока карточка на пальце, страницу прокручивать нельзя
        container.addEventListener('touchmove', onMove, { passive: false });
        container.addEventListener('touchend', onEnd, { passive: false });
        container.addEventListener('touchcancel', onCancel, { passive: false });
    }

    // --- drag-n-drop иерархии задач (todo37; доработка T-176) ---
    // Тот же приём, что у закладок: dataTransfer заполняется в JS (Firefox без setData
    // перетаскивание не начинает), слушатели делегированы на контейнер и переживают
    // ререндеры Blazor. Узел-источник помечен data-tree-task, зона сброса — data-tree-drop
    // (значение — id новой родительской задачи; пустая строка — «в корень»).
    //
    // T-176: пока задачу тащат, список сам прокручивается у верхнего и нижнего края —
    // иначе до дальней задачи (и до нижней полосы «в корень») мышь просто не доезжает:
    // в дереве из 500 задач конец списка на 16 000 px ниже видимой части.
    const TreeScrollEdge = 48;   // полоса у края, в которой начинается прокрутка, px
    const TreeScrollStep = 24;   // на сколько прокручивать за такт, px

    function initTreeDnd(containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container || container.dataset.ai2pTreeDnd) return;
        container.dataset.ai2pTreeDnd = '1';
        let dragged = null;
        let scrollTimer = null;
        let scrollBox = null;
        let pointerY = 0;

        function clearOver() {
            container.querySelectorAll('.ai2p-tree-over').forEach(function (el) {
                el.classList.remove('ai2p-tree-over');
            });
        }

        // ближайший прокручиваемый предок дерева (фрейм представления, панель эксплорера
        // или сама страница)
        function scroller() {
            let node = container.parentElement;
            while (node && node !== document.body) {
                const style = getComputedStyle(node);
                if (/(auto|scroll|overlay)/.test(style.overflowY) &&
                    node.scrollHeight > node.clientHeight + 2) {
                    return node;
                }
                node = node.parentElement;
            }
            return document.scrollingElement || document.documentElement;
        }

        function autoScrollStep() {
            if (!scrollBox) return;
            // дерево убрали из документа прямо во время перетаскивания (закрыли закладку):
            // dragend в этом случае не приходит, а таймер иначе крутил бы список сам собой
            if (!container.isConnected) { dragEnd(); return; }
            const page = scrollBox === document.scrollingElement ||
                         scrollBox === document.documentElement;
            const box = page ? { top: 0, bottom: window.innerHeight }
                             : scrollBox.getBoundingClientRect();
            // верхний край считается ПОД липкой полосой «в корень»: иначе, подводя к ней
            // задачу, список уезжал бы вверх и сбросить в корень стало бы нельзя
            const sticky = container.querySelector('.ai2p-tree-root-top');
            const top = sticky ? Math.max(box.top, sticky.getBoundingClientRect().bottom) : box.top;
            if (pointerY > top && pointerY < top + TreeScrollEdge) {
                scrollBox.scrollTop -= TreeScrollStep;
            } else if (pointerY > box.bottom - TreeScrollEdge &&
                       pointerY < box.bottom + TreeScrollEdge) {
                scrollBox.scrollTop += TreeScrollStep;
            }
        }

        // мышь надо слушать на всём документе: над пустым местом фрейма dragover
        // контейнера уже не приходит, а прокрутка нужна и там
        function trackPointer(e) { pointerY = e.clientY; }

        function dragBegin() {
            container.classList.add('ai2p-tree-dragging'); // зоны «в корень» видно
            scrollBox = scroller();
            document.addEventListener('dragover', trackPointer);
            if (!scrollTimer) scrollTimer = setInterval(autoScrollStep, 60);
        }

        function dragEnd() {
            container.classList.remove('ai2p-tree-dragging');
            document.removeEventListener('dragover', trackPointer);
            if (scrollTimer) { clearInterval(scrollTimer); scrollTimer = null; }
            scrollBox = null;
        }

        container.addEventListener('dragstart', function (e) {
            const node = e.target.closest('[data-tree-task]');
            if (!node) return;
            dragged = node.dataset.treeTask;
            e.dataTransfer.setData('text/plain', dragged);
            e.dataTransfer.effectAllowed = 'move';
            pointerY = e.clientY;
            dragBegin();
        });
        container.addEventListener('dragend', function () {
            dragged = null;
            clearOver();
            dragEnd();
        });
        container.addEventListener('dragover', function (e) {
            const zone = e.target.closest('[data-tree-drop]');
            if (!zone || zone.dataset.treeDrop === dragged) return;
            e.preventDefault(); // без этого курсор «запрещено» и drop не придёт
            e.dataTransfer.dropEffect = 'move';
            if (!zone.classList.contains('ai2p-tree-over')) {
                clearOver();
                zone.classList.add('ai2p-tree-over');
            }
        });
        container.addEventListener('dragleave', function (e) {
            const zone = e.target.closest('[data-tree-drop]');
            if (zone) zone.classList.remove('ai2p-tree-over');
        });
        container.addEventListener('drop', function (e) {
            const zone = e.target.closest('[data-tree-drop]');
            if (!zone) return;
            e.preventDefault();
            clearOver();
            dragEnd(); // dragend после сброса приходит не всегда — гасим прокрутку сами
            const from = e.dataTransfer.getData('text/plain') || dragged;
            dragged = null;
            if (!from || from === zone.dataset.treeDrop) return;
            dotnetRef.invokeMethodAsync('OnTreeDropAsync', from, zone.dataset.treeDrop);
        });

        // тот же перенос пальцем (T-193): без этого вертикальная прокрутка дерева на
        // телефоне превращалась в перетаскивание строки. Переносятся только строки,
        // которые разрешено двигать (data-tree-move="1", ТЗ гл. 6, этап 42).
        // Правил два (T-4-S1): за РУЧКУ [data-tree-grip] строка берётся сразу, за любое
        // другое место — прежним удержанием. Ручка — на телефоне единственный надёжный
        // способ: удержание браузер норовит понять как выделение текста или своё меню.
        const treeRule = {
            source: '[data-tree-task][data-tree-move="1"]',
            target: '[data-tree-drop]',
            over: 'ai2p-tree-over',
            sticky: '.ai2p-tree-root-top',
            begin: function (box) { box.classList.add('ai2p-tree-dragging'); },
            end: function (box) { box.classList.remove('ai2p-tree-dragging'); },
            drop: function (node, zone) {
                const from = node.dataset.treeTask;
                if (!from || from === zone.dataset.treeDrop) return;
                dotnetRef.invokeMethodAsync('OnTreeDropAsync', from, zone.dataset.treeDrop);
            },
        };
        initTouchDnd(container, [
            Object.assign({}, treeRule, { handle: '[data-tree-grip]', hold: 0 }),
            treeRule,
        ]);
    }

    // --- ленивые MD-превью на доске (ТЗ v1.17, todo20) ---
    // IntersectionObserver сообщает .NET о карточках, ставших видимыми: заполняются только
    // видимые, при листании заполнение продолжается. Повторный вызов после ререндера
    // подхватывает новые элементы .ai2p-lazy-md (наблюдение однократное на элемент).
    let lazyMdObserver = null;

    function observeLazyMd(dotnetRef) {
        if (!('IntersectionObserver' in window)) {
            // старый браузер: наблюдателя нет — просто грузим все карточки
            document.querySelectorAll('.ai2p-lazy-md[data-task-id]:not([data-observed])').forEach(function (el) {
                el.dataset.observed = '1';
                dotnetRef.invokeMethodAsync('OnCardVisibleAsync', el.dataset.taskId);
            });
            return;
        }
        if (!lazyMdObserver) {
            lazyMdObserver = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (!entry.isIntersecting) return;
                    lazyMdObserver.unobserve(entry.target);
                    dotnetRef.invokeMethodAsync('OnCardVisibleAsync', entry.target.dataset.taskId);
                });
            }, { rootMargin: '100px' }); // немного заранее, чтобы листание было плавным
        }
        document.querySelectorAll('.ai2p-lazy-md[data-task-id]:not([data-observed])').forEach(function (el) {
            el.dataset.observed = '1';
            lazyMdObserver.observe(el);
        });
    }

    function disconnectLazyMd() {
        if (lazyMdObserver) {
            lazyMdObserver.disconnect();
            lazyMdObserver = null;
        }
    }

    // --- ленивая дорисовка карточек колонки доски (T-128) ---
    // Каждая колонка листается сама (свой overflow-y, как в Trello). Долистали её до низа —
    // .NET дорисовывает следующую пачку карточек (OnColumnMoreAsync).
    //
    // Слушаем именно событие scroll КОЛОНКИ, а не видимость маячка: с IntersectionObserver
    // (первая редакция T-128) колонка дорисовывалась целиком — при каждом ререндере
    // MudDropContainer маячок на мгновение оказывался вверху колонки, наблюдатель считал его
    // видимым и просил следующую пачку, и так по кругу. Поймано живой проверкой в браузере:
    // в колонке из 520 задач в DOM оказывалось 520 карточек вместо 30.
    //
    // Ссылка на .NET хранится у элемента (WeakMap), а не в замыкании: досок на экране может
    // быть несколько (фреймы рабочей области). Слушатель ставится один раз на элемент —
    // Blazor при ререндере правит содержимое колонки, а сам элемент оставляет.
    const boardLazyRefs = new WeakMap();
    const BoardLazyTail = 400;   // «до низа осталось меньше» — пора тянуть следующую пачку
    const BoardLazyPause = 300;  // пауза после дорисовки: разметке нужно доехать от сервера

    function boardLazyMore(body) {
        if (body.dataset.lazyBusy === '1' || body.dataset.boardMore !== '1') return;
        if (body.scrollHeight - body.scrollTop - body.clientHeight > BoardLazyTail) return;
        const ref = boardLazyRefs.get(body);
        if (!ref) return;
        const before = body.scrollHeight;
        body.dataset.lazyBusy = '1';
        function done() {
            // разметка приходит от сервера отдельным пакетом — ждём и проверяем, что колонка
            // ДЕЙСТВИТЕЛЬНО выросла: иначе следующий заход сделает настоящая прокрутка
            setTimeout(function () {
                body.dataset.lazyBusy = '';
                if (body.scrollHeight > before) boardLazyMore(body);
            }, BoardLazyPause);
        }
        ref.invokeMethodAsync('OnColumnMoreAsync', body.dataset.boardLazy).then(done, done);
    }

    function initBoardLazy(containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.querySelectorAll('[data-board-lazy]').forEach(function (body) {
            boardLazyRefs.set(body, dotnetRef);
            if (!body.dataset.lazyInit) {
                body.dataset.lazyInit = '1';
                body.addEventListener('scroll', function () { boardLazyMore(body); }, { passive: true });
            }
            // пачка короче колонки (высокое окно, мелкие карточки) — прокрутки не будет,
            // а карточки показать надо: дотягиваем сразу
            boardLazyMore(body);
        });
    }

    // снять только свою доску: другие доски на экране должны продолжать работать
    function disposeBoardLazy(containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.querySelectorAll('[data-board-lazy]').forEach(function (body) {
            boardLazyRefs.delete(body);
        });
    }

    // --- перетаскивание колонок доски за заголовок (T-128) ---
    // Тот же приём, что у закладок и иерархии: dataTransfer заполняется в JS (Firefox без
    // setData перетаскивание не начинает), слушатели делегированы на контейнер доски и
    // переживают ререндеры Blazor. Источник — заголовок [data-board-drag], цель — колонка
    // [data-board-col]; место вставки подсвечивается по половине колонки, в которой курсор.
    // Пока не тянут заголовок (dragged пуст), обработчики молчат.
    // Карточки задач не перетаскиваются вовсе (T-193, второй заход): доска — единственное
    // место, где что-то тянут, и тянут только за заголовок колонки.
    function initBoardDnd(containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container || container.dataset.ai2pBoardDnd) return;
        container.dataset.ai2pBoardDnd = '1';
        let dragged = null;

        function clearOver() {
            container.querySelectorAll('.ai2p-board-before, .ai2p-board-after').forEach(function (el) {
                el.classList.remove('ai2p-board-before');
                el.classList.remove('ai2p-board-after');
            });
        }
        function insertBefore(col, ev) {
            const rect = col.getBoundingClientRect();
            return ev.clientX < rect.left + rect.width / 2;
        }

        container.addEventListener('dragstart', function (e) {
            const header = e.target.closest('[data-board-drag]');
            if (!header) {
                // тянут не заголовок колонки — переносить нечего: карточка задачи
                // перетаскиваемой больше не является (T-193, второй заход)
                return;
            }
            dragged = header.dataset.boardDrag;
            e.dataTransfer.setData('text/plain', dragged);
            e.dataTransfer.effectAllowed = 'move';
        });
        container.addEventListener('dragend', function () { dragged = null; clearOver(); });
        container.addEventListener('dragover', function (e) {
            if (!dragged) return; // тянут не заголовок колонки — доске это не интересно
            const col = e.target.closest('[data-board-col]');
            if (!col) return;
            e.preventDefault(); // без этого курсор «запрещено» и drop не придёт
            e.dataTransfer.dropEffect = 'move';
            clearOver();
            if (col.dataset.boardCol !== dragged) {
                col.classList.add(insertBefore(col, e) ? 'ai2p-board-before' : 'ai2p-board-after');
            }
        });
        container.addEventListener('drop', function (e) {
            if (!dragged) return;
            e.preventDefault();
            clearOver();
            const from = dragged;
            dragged = null;
            const col = e.target.closest('[data-board-col]');
            if (!col || col.dataset.boardCol === from) return;
            dotnetRef.invokeMethodAsync('OnColumnDropAsync', from, col.dataset.boardCol, insertBefore(col, e));
        });
    }

    // --- доска пальцем (T-193, мобильная версия) ---
    // Вертикальную прокрутку колонок ломало перетаскивание карточек: любое движение пальца
    // по карточке браузер (и сам MudBlazor своими touch-обработчиками) понимал как перенос.
    // Со второго захода T-193 карточка не перетаскивается вовсе — свайп по ней просто
    // листает колонку, и никакого правила для карточки здесь нет. Пальцем переносится
    // только ЗАГОЛОВОК колонки (жест «подержать и вести», тот же движок, что в дереве):
    // так порядок колонок меняется и на телефоне.
    function initBoardTouch(containerId, dotnetRef) {
        const container = document.getElementById(containerId);
        if (!container) return;
        initTouchDnd(container, [
            {
                source: '[data-board-drag]',
                target: '[data-board-col]',
                over: 'ai2p-touch-over',
                drop: function (header, col, x) {
                    const from = header.dataset.boardDrag;
                    if (!from || col.dataset.boardCol === from) return;
                    const rect = col.getBoundingClientRect();
                    dotnetRef.invokeMethodAsync('OnColumnDropAsync', from, col.dataset.boardCol,
                        x < rect.left + rect.width / 2);
                },
            },
        ]);
    }

    // --- копирование в буфер обмена (ссылка на задачу, ТЗ гл. 11) ---
    function copyText(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        // fallback для http-контекста без Clipboard API
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        return Promise.resolve();
    }

    // --- MD-редактор (ТЗ гл. 11) ---

    // отступ кода в тексте — ОДИН символ табуляции (Markdown: строка с отступом = блок кода)
    const MdTab = '\t';

    // предел размера картинки из буфера обмена (T-136): картинка едет к серверу одним
    // сообщением канала Blazor, у которого свой предел (32 МБ, Program.cs). Base64 длиннее
    // самого файла на треть, поэтому здесь — 20 МБ, и слишком большая честно отклоняется
    // сообщением, а не обрывом связи страницы с сервером
    const PasteMaxBytes = 20 * 1024 * 1024;

    // картинка из буфера обмена → .NET; общее для textarea и предпросмотра
    function mdSendPaste(ref, file) {
        if (!ref) return;
        if (file.size > PasteMaxBytes) {
            ref.invokeMethodAsync('OnPasteTooBigAsync', Math.round(PasteMaxBytes / 1024 / 1024));
            return;
        }
        const reader = new FileReader();
        reader.onload = function () {
            const base64 = String(reader.result).split(',')[1];
            const ext = ((file.type || 'image/png').split('/')[1] || 'png').replace('jpeg', 'jpg');
            const name = file.name && file.name !== 'image.png' ? file.name : 'clipboard.' + ext;
            ref.invokeMethodAsync('OnPasteImageAsync', name, base64);
        };
        reader.readAsDataURL(file);
    }

    // картинка в буфере обмена: Chrome/Edge отдают её через items, Safari/Firefox — иногда
    // только через files
    function mdClipboardImage(cd) {
        if (!cd) return null;
        for (const item of cd.items || []) {
            if (item.kind === 'file' && item.type.startsWith('image/')) {
                const file = item.getAsFile();
                if (file) return file;
            }
        }
        for (const f of cd.files || []) {
            if (f.type && f.type.startsWith('image/')) return f;
        }
        return null;
    }

    // перехват вставки картинок из буфера обмена: файл уходит в .NET (OnPasteImageAsync).
    // Ссылка на .NET хранится у самого элемента (dataset живёт, замыкание — нет): при
    // повторном показе редактора Blazor отдаёт ТОТ ЖЕ textarea с уже висящим слушателем,
    // а объектная ссылка к тому времени могла смениться (T-136: после закрытия формы
    // Ctrl+V переставал вставлять картинку — вызов уходил в снятую с учёта ссылку).
    const mdRefs = new WeakMap();

    // СОДЕРЖИМОЕ ПОЛЯ ТЕКСТА ВЕДЁТ JS, А НЕ BLAZOR (T-194) — как у редактируемого
    // предпросмотра (wysSync). Раньше поле рисовалось как <textarea value="@Value">, и
    // Blazor записывал значение обратно в элемент ПОСЛЕ каждого нажатия. На своей машине
    // это незаметно, а с телефона (канал с задержкой) ответ приходил, когда человек уже
    // набрал следующие символы, — и запись УСТАРЕВШЕГО значения их съедала. Жалоба
    // «ввод постоянно удаляет последний введённый символ» — ровно это.
    // Правило: содержимое ставится ОДИН раз при появлении элемента, дальше — только когда
    // значение изменилось ИЗВНЕ (force: очистка поля чата после отправки, кнопки панели).
    function mdSync(textareaId, dotnetRef, text, force) {
        const ta = document.getElementById(textareaId);
        if (!ta) return;
        mdRefs.set(ta, dotnetRef);
        if (ta.dataset.ai2pMd) {
            // своё же значение назад не пишем: набранное за время ответа не должно пропасть
            if (force && ta.value !== (text || '')) {
                const atEnd = ta.selectionStart === ta.value.length;
                ta.value = text || '';
                if (atEnd) ta.setSelectionRange(ta.value.length, ta.value.length);
            }
            return;
        }
        ta.dataset.ai2pMd = '1';
        ta.value = text || ''; // Blazor рисует поле пустым — наполняет его JS
        // Tab / Shift+Tab — отступ выделенных строк, а не переход на другое поле (T-136).
        // Новое значение уходит в .NET сразу (OnTextChangedAsync): oninput на программную
        // правку value не срабатывает
        ta.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab' || e.ctrlKey || e.altKey || e.metaKey) return;
            e.preventDefault();
            const value = mdIndent(textareaId, e.shiftKey);
            const ref = mdRefs.get(ta);
            if (value !== null && ref) ref.invokeMethodAsync('OnTextChangedAsync', value);
        });
        ta.addEventListener('paste', function (e) {
            const file = mdClipboardImage(e.clipboardData);
            if (!file) return;
            e.preventDefault();
            mdSendPaste(mdRefs.get(ta) || dotnetRef, file);
        });
    }

    // вставить текст в позицию курсора; возвращает новое значение textarea
    function mdInsert(textareaId, text) {
        const ta = document.getElementById(textareaId);
        if (!ta) return null;
        const start = ta.selectionStart ?? ta.value.length;
        const end = ta.selectionEnd ?? ta.value.length;
        ta.value = ta.value.slice(0, start) + text + ta.value.slice(end);
        const pos = start + text.length;
        ta.focus();
        ta.setSelectionRange(pos, pos);
        return ta.value;
    }

    // ВСТАВКА В ПОЛЕ MudBlazor ПО СЕЛЕКТОРУ (T-272, макроподстановки уведомлений).
    // mdInsert выше работает по id: так устроен MD-редактор, у которого textarea своя,
    // написанная руками. У MudTextField элемента с нашим id нет — MudBlazor кладёт
    // произвольные атрибуты на сам input (или textarea при Lines > 1), поэтому поле
    // ищется по data-атрибуту. Возвращает новое значение — его подхватывает .NET.
    function insertAtCaret(selector, text) {
        const el = document.querySelector(selector);
        if (!el) return null;
        const start = el.selectionStart ?? el.value.length;
        const end = el.selectionEnd ?? el.value.length;
        el.value = el.value.slice(0, start) + text + el.value.slice(end);
        const pos = start + text.length;
        el.focus();
        if (el.setSelectionRange) el.setSelectionRange(pos, pos);
        // Blazor слушает change/input у поля — без события набранное осталось бы только в DOM
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
        return el.value;
    }

    // обернуть выделение (жирный/курсив/код/ссылка); возвращает новое значение textarea
    function mdWrap(textareaId, before, after, placeholder) {
        const ta = document.getElementById(textareaId);
        if (!ta) return null;
        const start = ta.selectionStart ?? 0;
        const end = ta.selectionEnd ?? 0;
        const selected = ta.value.slice(start, end) || placeholder || '';
        ta.value = ta.value.slice(0, start) + before + selected + after + ta.value.slice(end);
        ta.focus();
        ta.setSelectionRange(start + before.length, start + before.length + selected.length);
        return ta.value;
    }

    // добавить префикс к каждой выделенной строке (заголовки, списки, цитаты)
    function mdPrefixLines(textareaId, prefix) {
        const ta = document.getElementById(textareaId);
        if (!ta) return null;
        const start = ta.selectionStart ?? 0;
        const end = ta.selectionEnd ?? 0;
        const lineStart = ta.value.lastIndexOf('\n', start - 1) + 1;
        const block = ta.value.slice(lineStart, end);
        const replaced = block.split('\n').map(l => prefix + l).join('\n');
        ta.value = ta.value.slice(0, lineStart) + replaced + ta.value.slice(end);
        ta.focus();
        const pos = lineStart + replaced.length;
        ta.setSelectionRange(pos, pos);
        return ta.value;
    }

    // блок кода (T-136): выделение оборачивается тремя апострофами ОТДЕЛЬНЫМИ строками —
    // иначе Markdown видит инлайн-код, а не блок. Возвращает новое значение textarea
    function mdCodeBlock(textareaId, placeholder) {
        const ta = document.getElementById(textareaId);
        if (!ta) return null;
        const start = ta.selectionStart ?? 0;
        const end = ta.selectionEnd ?? 0;
        const body = ta.value.slice(start, end) || placeholder || '';
        const before = ta.value.slice(0, start);
        const after = ta.value.slice(end);
        // ограждения обязаны начинаться со своей строки
        const lead = before.length === 0 || before.endsWith('\n') ? '' : '\n';
        const tail = after.length === 0 || after.startsWith('\n') ? '' : '\n';
        const block = lead + '```\n' + body + '\n```' + tail;
        ta.value = before + block + after;
        ta.focus();
        // курсор — на тексте внутри ограждений
        const pos = before.length + lead.length + 4;
        ta.setSelectionRange(pos, pos + body.length);
        return ta.value;
    }

    // отступ выделенных строк табуляцией (T-136): outdent — снять один отступ в начале
    // каждой строки (табуляция или до четырёх пробелов). Возвращает новое значение textarea
    function mdIndent(textareaId, outdent) {
        const ta = document.getElementById(textareaId);
        if (!ta) return null;
        const start = ta.selectionStart ?? 0;
        const end = ta.selectionEnd ?? 0;
        const lineStart = ta.value.lastIndexOf('\n', start - 1) + 1;
        // выделения нет и это не outdent — просто табуляция в позицию курсора
        if (start === end && !outdent) {
            return mdInsert(textareaId, MdTab);
        }
        const block = ta.value.slice(lineStart, Math.max(end, start));
        const lines = block.split('\n').map(function (line) {
            if (!outdent) return MdTab + line;
            if (line.startsWith(MdTab)) return line.slice(1);
            const spaces = line.match(/^ {1,4}/);
            return spaces ? line.slice(spaces[0].length) : line;
        });
        const replaced = lines.join('\n');
        ta.value = ta.value.slice(0, lineStart) + replaced + ta.value.slice(Math.max(end, start));
        ta.focus();
        ta.setSelectionRange(lineStart, lineStart + replaced.length);
        return ta.value;
    }

    // --- редактируемый предпросмотр MD-редактора (todo30_2) ---
    // Как в Trello и Notepad Windows 11: предпросмотр — единая редактируемая поверхность
    // (contenteditable с HTML от Markdig), кнопки панели применяют форматирование командами
    // браузера, изменённый HTML уходит в .NET (OnPreviewHtmlChangedAsync) и конвертируется
    // обратно в Markdown (MdHtml). Blazor рендерит div пустым и не трогает его содержимое.
    const wysState = {}; // id → { ref, range, media }
    let wysSelectionTracked = false;

    // выделение запоминается на selectionchange: клик по кнопке панели уводит фокус
    // из contenteditable, перед командой выделение восстанавливается
    function wysTrackSelection() {
        if (wysSelectionTracked) return;
        wysSelectionTracked = true;
        document.addEventListener('selectionchange', function () {
            const sel = document.getSelection();
            if (!sel || sel.rangeCount === 0) return;
            for (const id of Object.keys(wysState)) {
                const div = document.getElementById(id);
                if (!div) { delete wysState[id]; continue; }
                if (div.contains(sel.anchorNode)) wysState[id].range = sel.getRangeAt(0).cloneRange();
            }
        });
    }

    // синхронизация contenteditable со значением редактора: пересозданный Blazor div
    // инициализируется заново (OnAfterRenderAsync зовёт после каждого рендера), на живом
    // элементе повторный вызов ничего не делает; force — Value изменилось извне
    // (очистка поля чата после отправки, todo30_3) → перерисовать содержимое
    function wysSync(divId, dotnetRef, html, force) {
        const div = document.getElementById(divId);
        if (!div) return;
        if (div.dataset.ai2pWys) {
            const st = wysState[divId];
            if (st) st.ref = dotnetRef;
            if (force) {
                div.innerHTML = html;
                if (st) { st.range = null; st.media = null; }
            }
            return;
        }
        div.dataset.ai2pWys = '1';
        div.innerHTML = html;
        wysState[divId] = { ref: dotnetRef, range: null, media: null };
        wysTrackSelection();

        // Tab / Shift+Tab — отступ, а не переход на другое поле (T-136); в предпросмотре
        // отступ делает сам браузер (indent/outdent), в текстовом режиме — mdIndent
        div.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab' || e.ctrlKey || e.altKey || e.metaKey) return;
            e.preventDefault();
            document.execCommand(e.shiftKey ? 'outdent' : 'indent');
            wysNotify(divId);
        });

        // каждое изменение сразу в .NET (как oninput у textarea): «отправить» в чате
        // всегда видит актуальный текст, дебаунс с blur-сбросом подводил бы в Safari
        // (клик по кнопке там не уводит фокус)
        div.addEventListener('input', function () { wysNotify(divId); });
        // клик по картинке/видео запоминает цель для меню «ширина»
        div.addEventListener('click', function (e) {
            const media = e.target.closest('img,video');
            if (media && wysState[divId]) wysState[divId].media = media;
        });
        // вставка картинки из буфера — тот же путь, что в textarea (OnPasteImageAsync);
        // ссылка на .NET берётся из состояния: она обновляется при каждом wysSync
        div.addEventListener('paste', function (e) {
            const file = mdClipboardImage(e.clipboardData);
            if (!file) return;
            e.preventDefault();
            mdSendPaste((wysState[divId] || {}).ref || dotnetRef, file);
        });
    }

    function wysNotify(id) {
        const div = document.getElementById(id);
        const st = wysState[id];
        if (!div || !st) return;
        st.ref.invokeMethodAsync('OnPreviewHtmlChangedAsync', div.innerHTML);
    }

    function wysFocus(id) {
        const div = document.getElementById(id);
        const st = wysState[id];
        if (!div) return false;
        div.focus();
        if (st && st.range) {
            const sel = document.getSelection();
            sel.removeAllRanges();
            sel.addRange(st.range);
        }
        return true;
    }

    // простые команды форматирования (bold/italic/insertUnorderedList/insertOrderedList…)
    function wysExec(id, cmd, arg) {
        if (!wysFocus(id)) return;
        document.execCommand(cmd, false, arg || null);
        wysNotify(id);
    }

    // заголовок/цитата с поведением «переключателя»: повторное нажатие возвращает абзац;
    // цитату снимает outdent (formatBlock 'p' внутри blockquote лишь вложил бы абзац)
    function wysToggleBlock(id, tag) {
        if (!wysFocus(id)) return;
        const div = document.getElementById(id);
        const sel = document.getSelection();
        let node = sel && sel.anchorNode;
        let inside = false;
        while (node && node !== div) {
            if (node.nodeType === 1 && node.tagName.toLowerCase() === tag) { inside = true; break; }
            node = node.parentNode;
        }
        if (tag === 'blockquote' && inside) {
            document.execCommand('outdent');
        } else {
            document.execCommand('formatBlock', false, inside ? 'p' : tag);
        }
        wysNotify(id);
    }

    // инлайн-код: execCommand такого не умеет — вставка <code> поверх выделения
    function wysWrapCode(id, placeholder) {
        if (!wysFocus(id)) return;
        const sel = document.getSelection();
        const text = sel && !sel.isCollapsed ? sel.toString() : (placeholder || '');
        const esc = text.replace(/&/g, '&amp;').replace(/</g, '&lt;');
        document.execCommand('insertHTML', false, '<code>' + esc + '</code>');
        wysNotify(id);
    }

    // блок кода в предпросмотре (T-136): <pre><code> поверх выделения — обратная
    // конвертация (MdHtml) вернёт его тремя апострофами
    function wysCodeBlock(id, placeholder) {
        if (!wysFocus(id)) return;
        const sel = document.getSelection();
        const text = sel && !sel.isCollapsed ? sel.toString() : (placeholder || '');
        const esc = text.replace(/&/g, '&amp;').replace(/</g, '&lt;');
        document.execCommand('insertHTML', false, '<pre><code>' + esc + '</code></pre><p><br></p>');
        wysNotify(id);
    }

    // отступ выделения в предпросмотре — командами браузера (T-136)
    function wysIndent(id, outdent) {
        if (!wysFocus(id)) return;
        document.execCommand(outdent ? 'outdent' : 'indent');
        wysNotify(id);
    }

    function wysCreateLink(id, url) {
        if (!wysFocus(id)) return;
        const sel = document.getSelection();
        if (sel && !sel.isCollapsed) {
            document.execCommand('createLink', false, url);
        } else {
            const esc = url.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
            document.execCommand('insertHTML', false, '<a href="' + esc + '">' + esc + '</a>');
        }
        wysNotify(id);
    }

    // вставка готового HTML (картинка/видео после закачки) в позицию курсора
    function wysInsertHtml(id, html) {
        if (!wysFocus(id)) return;
        document.execCommand('insertHTML', false, html);
        wysNotify(id);
    }

    // ширина выделенной (последней кликнутой) картинки/видео — атрибутом width=N%
    function wysSetWidth(id, percent) {
        const st = wysState[id];
        const div = document.getElementById(id);
        if (!st || !div) return;
        let media = st.media && div.contains(st.media) ? st.media : null;
        if (!media) media = div.querySelector('img,video'); // одна картинка — очевидная цель
        if (!media) return;
        media.setAttribute('width', percent + '%');
        media.removeAttribute('height');
        wysNotify(id);
    }

    // --- консоль задания (ТЗ v1.45, todo37_3): прокрутка к последней строке ---
    // Прокручиваем только когда пользователь и так внизу: он мог отмотать вверх,
    // чтобы прочитать старую строку, — дёргать его прокруткой нельзя.
    function consoleScroll(id) {
        const box = document.getElementById(id);
        if (!box) return;
        const atBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 60;
        if (atBottom) box.scrollTop = box.scrollHeight;
    }

    // --- ПРЕВЬЮ ОБЪЕКТОВ (T-276): гасим картинку, которой на диске нет ---
    //
    // Адрес превью строится ПО РАСШИРЕНИЮ пути, без обращения к серверу (на список в сотню
    // строк это была бы сотня запросов ради того, что браузер и так выяснит, забирая
    // картинку). Значит, файла может не оказаться — и вместо превью в строке торчал бы
    // битый значок браузера. Здесь картинка на событии error просто прячется, и наружу
    // выходит значок «нет изображения», лежащий под ней.
    //
    // Ставится ОДИН слушатель на элемент (пометка в data-атрибуте), поэтому вызывать после
    // каждой отрисовки списка безопасно: Blazor держит те же элементы, повторное
    // подключение их пропускает. Загруженные помечаются data-pic-ok="1" — по этой пометке
    // живая проверка отличает «картинка доехала» от «в разметке адрес есть».
    function initPics() {
        document.querySelectorAll('img[data-ai2p-pic]:not([data-pic-wired])').forEach(function (img) {
            img.dataset.picWired = '1';
            img.addEventListener('load', function () {
                if (img.naturalWidth > 0) img.dataset.picOk = '1';
            });
            img.addEventListener('error', function () {
                img.style.display = 'none';
                const box = img.closest('.ai2p-pic');
                if (box) box.setAttribute('data-object-pic-has', '0');
            });
            // картинка из кеша могла загрузиться ДО того, как слушатель повешен: событие
            // load для неё уже не придёт, а помечать её надо
            if (img.complete && img.naturalWidth > 0) img.dataset.picOk = '1';
        });
    }

    // --- ЧТЕНИЕ ДОКУМЕНТАЦИИ: переходы ВНУТРИ закладки (T-17-S1) ---
    //
    // Текст страницы вставлен готовым HTML, поэтому обычного @onclick у ссылки нет.
    // Слушатель ставится ОДИН на всю область текста и разбирает клик сам: ссылка с
    // data-doc-link (её пометил DocLinks.Mark) — это переход внутри документации, он
    // перехватывается и уезжает в компонент; всё остальное браузер отрабатывает как обычно.
    // Слушатель переживает смену страницы: сама область при этом остаётся той же.
    function initDocLinks(id, ref) {
        const box = document.getElementById(id);
        if (!box || box.dataset.ai2pDoc === '1') return;
        box.dataset.ai2pDoc = '1';
        box.addEventListener('click', function (e) {
            const a = e.target && e.target.closest ? e.target.closest('a[data-doc-link]') : null;
            if (!a || !box.contains(a)) return;
            e.preventDefault();
            ref.invokeMethodAsync('OnDocLinkAsync', a.getAttribute('data-doc-link'));
        });
    }

    // новая страница показывается С НАЧАЛА: без этого переход по ссылке из середины
    // длинного документа оставляет читателя посреди следующего
    function docScrollTop(id) {
        const box = document.getElementById(id);
        if (!box) return;
        let p = box.parentElement;
        while (p && !p.classList.contains('ai2p-tabview')) p = p.parentElement;
        (p || box).scrollTop = 0;
    }

    // выход из предпросмотра: вернуть текущий HTML и снять состояние
    function wysClose(id) {
        const div = document.getElementById(id);
        delete wysState[id];
        if (div) delete div.dataset.ai2pWys;
        return div ? div.innerHTML : null;
    }

    // --- РАМКА КРОПА кадра датасета LoRA (T-12-S1) ---
    //
    // Режет, ужимает и переводит формат САМ БРАУЗЕР (canvas). Это не «сделали проще, чем
    // надо»: рамку человек тянет здесь же, декодированная картинка уже лежит в памяти
    // вкладки, а на сервере ради тех же трёх операций пришлось бы завести целую библиотеку
    // работы с изображениями (System.Drawing на Linux не работает вовсе, ImageSharp — лишняя
    // зависимость). Сервер при этом ПРОВЕРЯЕТ присланное своими пределами: размер в байтах
    // по байтам, размер в пикселях по заголовку файла. Доверия к присылающей стороне нет.
    //
    // Тянутся ДВА угла — левый верхний и правый нижний, как сказано в задании. Координаты
    // рамки живут в пикселях ИСХОДНОЙ картинки, а не экрана: окно можно растянуть, картинка
    // показана уменьшенной, и хранить экранные пиксели значило бы терять кроп при каждом
    // изменении размера окна.
    const loraCrops = {};

    /// при первом показе рамка — ВСЯ картинка: человеку, которому резать не нужно,
    /// не должно быть нужно ничего тянуть. Размеры знает только загруженная картинка
    function whenReady(st) {
        const ready = function () {
            if (!st.img.naturalWidth) return;
            st.crop = { x: 0, y: 0, w: st.img.naturalWidth, h: st.img.naturalHeight };
            place(st);
        };
        if (st.img.complete && st.img.naturalWidth) ready();
        else st.img.addEventListener('load', ready, { once: true });
    }

    function initLoraCrop(id) {
        const box = document.getElementById(id);
        if (!box) return false;
        const img = box.querySelector('[data-lora-img]');
        const frame = box.querySelector('[data-lora-frame]');
        if (!img || !frame) return false;
        const src = img.getAttribute('src') || '';
        const known = loraCrops[id];
        if (known && known.img === img) {
            // ТОТ ЖЕ исходник — всё уже настроено. А вот ДРУГОЙ (в форме нажали «загрузить»
            // второй раз, с другим адресом) обязан сбросить рамку: иначе она осталась бы
            // от прошлой картинки, и кроп резал бы не то. Обработчики при этом не
            // навешиваются заново — они держат ЭТОТ объект состояния и продолжают работать
            if (known.src !== src) {
                known.src = src;
                known.crop = null;
                known.frame.style.display = 'none';
                whenReady(known);
            }
            return true;
        }
        box.dataset.ai2pCrop = '1';
        const st = { box: box, img: img, frame: frame, src: src, crop: null, drag: null };
        loraCrops[id] = st;
        whenReady(st);
        window.addEventListener('resize', function () { place(st); });

        box.querySelectorAll('[data-lora-handle]').forEach(function (handle) {
            handle.addEventListener('pointerdown', function (e) {
                if (!st.crop) return;
                st.drag = handle.dataset.loraHandle;
                handle.setPointerCapture(e.pointerId);
                e.preventDefault();
                e.stopPropagation();
            });
            handle.addEventListener('pointermove', function (e) {
                if (st.drag !== handle.dataset.loraHandle || !st.crop) return;
                drag(st, e);
                e.preventDefault();
            });
            const stop = function (e) {
                if (st.drag !== handle.dataset.loraHandle) return;
                st.drag = null;
                try { handle.releasePointerCapture(e.pointerId); } catch (err) { /* уже отпущен */ }
            };
            handle.addEventListener('pointerup', stop);
            handle.addEventListener('pointercancel', stop);
        });
        return true;
    }

    // экранный масштаб картинки: показана она уменьшенной по ширине окна
    function loraScale(st) {
        return st.img.naturalWidth ? st.img.clientWidth / st.img.naturalWidth : 1;
    }

    function drag(st, e) {
        const rect = st.img.getBoundingClientRect();
        const scale = loraScale(st) || 1;
        // МЕНЬШЕ 16 пикселей рамку не даём: схлопнутую в точку рамку уже не растянуть —
        // ухватиться будет не за что, и форму придётся закрывать и открывать заново
        const min = 16;
        let x = Math.round((e.clientX - rect.left) / scale);
        let y = Math.round((e.clientY - rect.top) / scale);
        x = Math.max(0, Math.min(st.img.naturalWidth, x));
        y = Math.max(0, Math.min(st.img.naturalHeight, y));
        const c = st.crop;
        if (st.drag === 'tl') {
            const right = c.x + c.w;
            const bottom = c.y + c.h;
            c.x = Math.min(x, right - min);
            c.y = Math.min(y, bottom - min);
            c.w = right - c.x;
            c.h = bottom - c.y;
        } else {
            c.w = Math.max(min, x - c.x);
            c.h = Math.max(min, y - c.y);
            c.w = Math.min(c.w, st.img.naturalWidth - c.x);
            c.h = Math.min(c.h, st.img.naturalHeight - c.y);
        }
        place(st);
    }

    function place(st) {
        if (!st.crop) return;
        const scale = loraScale(st);
        st.frame.style.left = (st.crop.x * scale) + 'px';
        st.frame.style.top = (st.crop.y * scale) + 'px';
        st.frame.style.width = (st.crop.w * scale) + 'px';
        st.frame.style.height = (st.crop.h * scale) + 'px';
        st.frame.style.display = 'block';
        // размеры показываем прямо на рамке: «сколько получится» — это первое, что
        // спрашивают, а считать в уме отношение экранных пикселей к исходным нельзя
        st.box.dataset.loraCrop = st.crop.x + ',' + st.crop.y + ',' + st.crop.w + ',' + st.crop.h;
    }

    // текущая рамка в пикселях исходной картинки — форме, чтобы показать размер кадра
    function loraCropInfo(id) {
        const st = loraCrops[id];
        if (!st || !st.crop) return null;
        return {
            x: st.crop.x, y: st.crop.y, w: st.crop.w, h: st.crop.h,
            fullW: st.img.naturalWidth, fullH: st.img.naturalHeight
        };
    }

    // ГОТОВЫЙ КАДР: вырезать рамку, ужать до пределов настроек и перевести в формат.
    // Соотношение сторон НЕ меняется: коэффициент один на обе стороны и берётся МЕНЬШИЙ из
    // двух — тогда кадр помещается в заданный размер целиком, а не растягивается по одной
    // стороне. В килобайты укладываемся уменьшением (у PNG сжимать больше нечем; у JPEG
    // сначала пробуем качеством — терять пиксели дороже, чем точность цвета).
    function loraCropResult(id, maxW, maxH, maxKb, format) {
        const st = loraCrops[id];
        if (!st || !st.crop || !st.img.naturalWidth) return null;
        const c = st.crop;
        const type = format === 'jpeg' ? 'image/jpeg' : 'image/png';
        let scale = Math.min(1, maxW / c.w, maxH / c.h);
        let quality = 0.92;
        let last = null;
        for (let attempt = 0; attempt < 12; attempt++) {
            const w = Math.max(1, Math.round(c.w * scale));
            const h = Math.max(1, Math.round(c.h * scale));
            const canvas = document.createElement('canvas');
            canvas.width = w;
            canvas.height = h;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(st.img, c.x, c.y, c.w, c.h, 0, 0, w, h);
            const url = canvas.toDataURL(type, quality);
            const data = url.substring(url.indexOf(',') + 1);
            last = { data: data, width: w, height: h, bytes: Math.round(data.length * 3 / 4) };
            if (last.bytes <= maxKb * 1024) return last;
            if (type === 'image/jpeg' && quality > 0.5) quality -= 0.15; else scale *= 0.85;
        }
        return last;   // не уложились — отдаём последнее, отказ напишет форма и сервер
    }

    function disposeLoraCrop(id) {
        delete loraCrops[id];
    }

    // --- АВТОМАТИЧЕСКОЕ ПЕРЕЖАТИЕ КАДРА ПОД ДРУГОЙ ДАТАСЕТ (T-57-S0) ---
    //
    // Работает тем же холстом и по той же причине, что и кроп: картинка уже лежит в памяти
    // вкладки, а на сервере ради этого пришлось бы завести библиотеку работы с изображениями.
    // Отличие одно, и оно из задания: РЕЗАТЬ НЕЛЬЗЯ и соотношение сторон менять нельзя —
    // картинка вписывается в кадр целиком по большей стороне и ставится по центру, а поля
    // закрашиваются цветом из настройки. Увеличивать тоже не будем (масштаб не больше 1):
    // растянутый на весь кадр мелкий исходник — это выдуманные пиксели, а не кадр датасета.
    //
    // АЛЬФА: у PNG цвет кладётся как есть (белый с нулевой альфой = прозрачные поля), а у
    // JPEG альфы нет вовсе, и прозрачное холст отдаёт ЧЁРНЫМ — поэтому там альфа отбрасывается
    // и поля выходят того же цвета, но непрозрачные (по умолчанию белые).
    function padColor(value, opaque) {
        let hex = String(value || '').trim().replace(/^#/, '');
        if (!/^[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/.test(hex)) hex = 'FFFFFF00';
        if (hex.length === 6) hex += 'FF';
        const n = function (at) { return parseInt(hex.substr(at, 2), 16); };
        const a = opaque ? 1 : n(6) / 255;
        return 'rgba(' + n(0) + ',' + n(2) + ',' + n(4) + ',' + a + ')';
    }

    function loraFitImage(url, maxW, maxH, maxKb, format, fill) {
        return new Promise(function (resolve) {
            const img = new Image();
            img.crossOrigin = 'anonymous';
            img.onerror = function () { resolve(null); };
            img.onload = function () {
                const sw = img.naturalWidth, sh = img.naturalHeight;
                if (!sw || !sh) { resolve(null); return; }
                const jpeg = format === 'jpeg';
                const type = jpeg ? 'image/jpeg' : 'image/png';
                const color = padColor(fill, jpeg);
                let box = 1;             // во сколько раз ужимаем САМ КАДР, если не влезли в КБ
                let quality = 0.92;
                let last = null;
                for (let attempt = 0; attempt < 12; attempt++) {
                    const cw = Math.max(1, Math.round(maxW * box));
                    const ch = Math.max(1, Math.round(maxH * box));
                    // один коэффициент на обе стороны и МЕНЬШИЙ из двух: так картинка
                    // помещается целиком, а не растягивается по одной стороне
                    const scale = Math.min(1, cw / sw, ch / sh);
                    const w = Math.max(1, Math.round(sw * scale));
                    const h = Math.max(1, Math.round(sh * scale));
                    const canvas = document.createElement('canvas');
                    canvas.width = cw;
                    canvas.height = ch;
                    const ctx = canvas.getContext('2d');
                    ctx.fillStyle = color;
                    ctx.fillRect(0, 0, cw, ch);
                    ctx.drawImage(img, 0, 0, sw, sh,
                        Math.round((cw - w) / 2), Math.round((ch - h) / 2), w, h);
                    const url2 = canvas.toDataURL(type, quality);
                    const data = url2.substring(url2.indexOf(',') + 1);
                    last = { data: data, width: cw, height: ch, bytes: Math.round(data.length * 3 / 4) };
                    if (last.bytes <= maxKb * 1024) return resolve(last);
                    if (jpeg && quality > 0.5) quality -= 0.15; else box *= 0.85;
                }
                resolve(last);   // не уложились — отдаём последнее, отказ напишет форма и сервер
            };
            img.src = url;
        });
    }

    return {
        initSplitter: initSplitter,
        initFrameSplitters: initFrameSplitters,
        initTabDnd: initTabDnd,
        initTabScroll: initTabScroll,
        scrollTabs: scrollTabs,
        initStrips: initStrips,
        scrollStrip: scrollStrip,
        initTreeDnd: initTreeDnd,
        observeLazyMd: observeLazyMd,
        disconnectLazyMd: disconnectLazyMd,
        initBoardLazy: initBoardLazy,
        disposeBoardLazy: disposeBoardLazy,
        initBoardDnd: initBoardDnd,
        initBoardTouch: initBoardTouch,
        copyText: copyText,
        mdSync: mdSync,
        mdInsert: mdInsert,
        insertAtCaret: insertAtCaret,
        mdWrap: mdWrap,
        mdPrefixLines: mdPrefixLines,
        mdCodeBlock: mdCodeBlock,
        mdIndent: mdIndent,
        wysSync: wysSync,
        wysExec: wysExec,
        wysToggleBlock: wysToggleBlock,
        wysWrapCode: wysWrapCode,
        wysCodeBlock: wysCodeBlock,
        wysIndent: wysIndent,
        wysCreateLink: wysCreateLink,
        wysInsertHtml: wysInsertHtml,
        wysSetWidth: wysSetWidth,
        wysClose: wysClose,
        consoleScroll: consoleScroll,
        initPics: initPics,
        initDocLinks: initDocLinks,
        docScrollTop: docScrollTop,
        initLoraCrop: initLoraCrop,
        loraCropInfo: loraCropInfo,
        loraCropResult: loraCropResult,
        disposeLoraCrop: disposeLoraCrop,
        loraFitImage: loraFitImage,
    };
})();
