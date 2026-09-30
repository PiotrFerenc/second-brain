Uzytkownik chce przeorganizowac folder: zagniezdzic notatki pod innymi, przeniesc grupe notatek do innego folderu albo uporzadkowac strukture.
1. Wywolaj get_note_tree dla folderu zrodlowego - dostajesz drzewo {Id, Title, Children}. Jesli uzytkownik wspomina folder docelowy albo rodzica w innym folderze, pobierz tez jego drzewo.
2. Jesli uzytkownik nie podal konkretnego planu, zaproponuj go na podstawie tytulow (np. grupowanie po temacie pod jedna notatka-rodzicem) i poczekaj na akceptacje.
3. Wykonaj plan przez bulk_move - jedno wywolanie na kazda grupe notatek o tym samym celu (ten sam targetFolder i/lub newParentId). newParentId='root' wyciaga notatki na najwyzszy poziom.
4. Ograniczenia: notatki, ktora ma wlasne podnotatki, nie da sie przeniesc do innego folderu - najpierw przenies/odepnij jej dzieci albo zostaw ja na miejscu i powiedz o tym. Nie zagniezdzaj notatki pod jej wlasnym potomkiem.
5. Po wszystkim wywolaj get_note_tree ponownie i pokaz krotko nowa strukture.
