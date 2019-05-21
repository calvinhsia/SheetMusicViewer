Choose a path to a root folder which contains PDF music files. PDF files can be 0-N pages.
Some books scan to multiple PDFs. e.g. I have several books > 100 pages and scan them in smaller chunks to PDF. 
Some of the bindings of these books are well-worn, so scanning works best in smaller chunks. 

Also, the document sheet feeder works best with the non-bound edge leading, so you can mark them with a Rotation settings, which will be persisted.
Name these book0.pdf, book1.pdf, etc. and they will be treated as a single book with multiple volumes
Each 'book' has a .BMK file written alongside. This contains the LastPageno opened, Rotation, TOC, Favorites, inking, and multi-volume info.
You can import/export TOC to the clipboard and add info like composer, date, etc.
The BMK file is read upon program start, or upon setting a new 'Music Folder Path' (you can set the path to be the same to force a re-read of all BMKs)
The 1st page of A PDF file (or a set of PDF with consecutive numbered names) is used as the 'icon' to represent the music in the Choose dialog, the Table Of Contents Dialog, etc.
So if the PDF has a cover page it will be the icon. For a singles folder, you can create a custom PDF title page. 
You can use any prorgam that can print, and print to the "Microsoft Print to PDF" printer. That will create a PDF document.
Make sure it's named earlier in alpahbetical order than any other single in the folder

A subfolder called 'Hidden' will not be searched.
A subfolder with name ending in 'Singles', like 'GershwinSingles' will treat each contained PDF as a single song (with possibly 1-N pages).
A singles folder is maintained alphabetically, with the 1st page of the 1st song being the icon. As items are added/removed/renamed within the singles folder, they are dynamically
added to the TOC, with associated favorites/inking following.

