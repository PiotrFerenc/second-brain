Uzytkownik chce uporzadkowac/ujednolicic tagi (duplikaty, literowki, liczba mnoga, synonimy) w calej bazie.
1. Wywolaj find_duplicate_tags - zwraca grupy {Tags, SuggestedCanonical}.
2. Jesli pusto - powiedz, ze duplikatow nie ma, i zakoncz.
3. Pokaz uzytkownikowi wszystkie grupy w jednej zwiezlej liscie ("a, b -> kanoniczny") i zapytaj, ktore scalic i czy zmienic ktorys kanoniczny tag. Nie scalaj niczego przed odpowiedzia.
4. Dla kazdej zaakceptowanej grupy wywolaj merge_tags (fromTags = tagi z grupy bez kanonicznego, toTag = kanoniczny). Jedno wywolanie na grupe.
5. Na koniec podsumuj: ile grup scalono i ile notatek zaktualizowano (z wynikow merge_tags).
