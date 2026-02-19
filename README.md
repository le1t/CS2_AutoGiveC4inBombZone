# CS2_AutoGiveC4inBombZone

Плагин автоматически находит бомбовые зоны (func_bomb_target) на карте и с заданным интервалом проверяет игроков. Если живой террорист находится в бомбовой зоне и у него нет C4 – ему выдаётся бомба. Также реагирует на событие входа в зону. Плагин отключает выдачу во время установки и после взрыва бомбы.


# https://www.youtube.com/watch?v=8haAs5mxHSQ

# Требования
```
CounterStrikeSharp API версии 362 или выше
.NET 8.0 Runtime
```

# Конфигурационные параметры
```
css_autogivec4_enable <0/1>, def.=1 – Включение/выключение плагина.
css_autogivec4_nogive_ground <0/1>, def.=0 – Не выдавать C4, если она лежит на земле (чтобы избежать дублирования).
css_autogivec4_check_interval <0.1-10.0>, def.=1.0 – Интервал проверки положения игроков в секундах.
css_autogivec4_debug <0/1>, def.=0 – Режим отладки с подробными логами.
css_autogivec4_loglevel <0-5>, def.=4 – Уровень логирования (0-Trace,1-Debug,2-Info,3-Warning,4-Error,5-Critical).
```

# Консольные команды
```
css_autogivec4_help – Показать справку по плагину.
css_autogivec4_settings – Показать текущие настройки и статус.
css_autogivec4_test – Выполнить тестовое сканирование зон и проверку игроков.
css_autogivec4_reload – Перезагрузить конфигурацию и повторно найти зоны.
css_autogivec4_setenabled <0/1> – Установить значение css_autogivec4_enable.
css_autogivec4_setnogiveground <0/1> – Установить значение css_autogivec4_nogive_ground.
css_autogivec4_setcheckinterval <сек> – Установить интервал проверки (0.1–10.0).
css_autogivec4_setdebug <0/1> – Установить режим отладки.
css_autogivec4_setloglevel <0-5> – Установить уровень логирования.
```


# ЭТОТ ПЛАГИН ФОРК ЭТОГО ПЛАГИНА https://hlmod.net/resources/autogive-c4-in-bombzone.381/
