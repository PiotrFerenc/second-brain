Uzytkownik chce znalezc i posprzatac zduplikowane notatki lezace w roznych folderach.
1. Wywolaj find_duplicate_notes (domyslny prog 0.92; nizszy, np. 0.85, gdy uzytkownik chce luzniejszego dopasowania). Zwraca pary {A:{Id,Title}, B:{Id,Title}, Similarity} - BEZ nazw folderow.
2. Jesli pusto - powiedz, ze duplikatow nie ma, i zakoncz.
3. Dla kazdej pary ustal foldery: search_notes z tytulem notatki (bez folderu) i dopasuj po Id. Potem get_note dla obu, zeby porownac tresc.
4. Pokaz uzytkownikowi pary (tytul, folder, podobienstwo, czym sie roznia) i zaproponuj dla kazdej: zostaw obie / usun jedna / scal.
5. Wykonaj decyzje:
   - usun jedna: trash_note (albo bulk_trash dla wielu w jednym folderze),
   - scal: edit_note na notatce, ktora zostaje, z polaczona trescia (bez utraty faktow z drugiej), potem trash_note na drugiej.
6. Podsumuj, co zrobiono. Usuniete notatki sa w koszu - mozna je przywrocic.
