-- "watermark" is now what "fullBleed" draws in the dark theme.
UPDATE `user_preferences` SET `preference_value` = 'fullBleed'
WHERE `preference_key` = 'ui.dailyImage' AND `preference_value` = 'watermark';
