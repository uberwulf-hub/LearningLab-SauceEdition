mkdir -p /var/log/proxy /etc/proxy
cat > /etc/proxy/blocked-sites.txt <<'TXT'
# sites the office proxy refuses (one name per line)
ads.tracker.example
TXT
cat > /var/log/proxy/access.log <<LOG
2026-04-04 09:02:11 client=10.10.25.31 method=GET host=news.example status=200 bytes_out=612 bytes_in=48211
2026-04-04 09:15:40 client=10.10.25.40 method=GET host=mail.{{CO_DOMAIN}} status=200 bytes_out=1022 bytes_in=90233
2026-04-04 10:31:05 client=10.10.25.33 method=POST host=forms.example status=200 bytes_out=2840 bytes_in=512
2026-04-04 11:47:52 client=10.10.25.31 method=GET host=weather.example status=200 bytes_out=588 bytes_in=21004
2026-04-04 14:20:13 client=10.10.25.33 method=GET host=shop.example status=200 bytes_out=731 bytes_in=150876
2026-04-05 {{TIME}} client=10.10.25.40 method=POST host={{DEST}} status=200 bytes_out={{BYTES}} bytes_in=340
2026-04-05 08:58:30 client=10.10.25.31 method=GET host=news.example status=200 bytes_out=605 bytes_in=47710
LOG
