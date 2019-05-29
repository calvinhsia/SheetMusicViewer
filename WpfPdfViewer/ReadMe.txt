PDF Music Viewer by Calvin Hsia 2019

I have a few hundred piano music books, singles, etc. that I've collected over the last several decades.
Most of the books were "perfect bound" which means they didn't stay open on the piano music stand.
Many decades ago I chopped off the bindings of the books and rebound them using plastic comb binding. This made playing the music much easier on the piano.
Since then I bought my own book binding cutter (HFS New Heavy Duty Guillotine Paper Cutter) and comb binding machine (Marigold 19-Hole Letter Size Comb Ring Binding Machine) 
to rebind more books.

About 3 months ago, I obtained a Microsoft Surface Book 2, which had a display large enough to trigger me digitizing my music collection.
I used a few Xerox WorkCentre 7855i or similar models available at work.
The doc sheetfeeder allowed me to scan up to 100 pages at a time. However, because of the age and wear and tear of the books (putting on the piano, turning pages), the pages were pretty worn.
The binding edges of the pages were much less uniformly smooth (some had residual adhesive) than the opposite edge because of the binding, so I fed them smooth edge first (upside down) through the document feeder.
Luckily it's pretty simple to have software rotate the page.
Many of my books are quite old, with disintegrating pages.
I tried various software sheet music viewers available, but wasn't at all satisfied, so I wrote my own to view my 30,000 pages of music.

Choose a path to a root folder which contains PDF music files. PDF files can be 0-N pages. 
The PDFs are never altered by the program. All data is stored in the BMKs. However, the program needs write permission to write the BMK files.
Some books scan to multiple PDFs. e.g. I have several books > 100 pages and scan them in smaller chunks to PDF. 
Some of the bindings of these books are well-worn, so scanning works best in smaller chunks. 
If you have multiple named files e.g. Book1.pdf, Book2.pdf, etc., then they will be treated as one entire book. The root name is the 1st one without any trailing 0 or 1.
The first one of a series:
	1. does not end in a digit "book.pdf" root name = "book"
	2. does end in a digit  "book0.pdf" root name = "book"

Subsequent ones must have the same root name as the 1st and must have a digit after the rotoname e.g. "book1.pdf", "book1a.pdf", "book2.pdf". As long as they sort in order, the digits don't matter.
Thus, book1, book1a, book2 are all treated together as one
but not SonatenI, SonatenI1, SonatenII: this is 2 books: "SonatenI" and "SonatenI1" are the 1st Sonaten, and "SonatenII" is the second.
This allows rescan of missing pages without needing to renumber subsequent volumes.

Also, the document sheet feeder works best with the non-bound edge leading, so you can mark them with a Rotation settings, which will be persisted.
Name these book0.pdf, book1.pdf, etc. and they will be treated as a single book with multiple volumes
Each 'book' has a .BMK file written alongside. This contains the LastPageno opened, Rotation, TOC, Favorites, inking, and multi-volume info.
You can import/export TOC to the clipboard and add info like composer, date, etc.
The BMK file is read upon program start, or upon setting a new 'Music Folder Path' (you can set the path to be the same to force a re-read of all BMKs)
The 1st page of A PDF file (or a set of PDF with consecutive numbered names) is used as the 'icon' to represent the music in the Choose dialog, the Table Of Contents Dialog, etc.
So if the PDF has a cover page it will be the icon. For a singles folder, you can create a custom PDF title page.
You can use any program that can print, and print to the "Microsoft Print to PDF" printer. That will create a PDF document.
Make sure it's named earlier in alpahbetical order than any other single in the folder


A subfolder called 'Hidden' will not be searched.
A subfolder with name ending in 'Singles', like 'GershwinSingles' will treat each contained PDF as a single song (with possibly 1-N pages).
A singles folder is maintained alphabetically, with the 1st page of the 1st song being the icon. As items are added/removed/renamed within the singles folder, they are dynamically
added to the TOC, with associated favorites/inking following.

There are 2 display modes: single page at a time and 2 pages (side by side) per screen.
The thumb left and right arrows at the top move a screenful at a time. (if there are favorites in the currently open book, they jump to the next favorite left or right, if any)
The left-right arrows move the page by 1 screenful.
The bottom quarter of the display is used to do page turning by click or touch. In 1 page per screen mode, the right half will move right 1 page, and the left half will move left 1 page.
In 2 page per screen mode, the bottom quareter is divided into 4 quarters. The outer 2 quarters will advance 1 screenful (2 pages), and the inner quarters will advance 1 page.
E.g. from showing page 3 on the left and 4 on the right, to page 4 on the left to page 5 on the right. This allows right hand pages to be shown on the left, and repeats, etc. to be seen more easily.
The top 3/4 of the page is used for moving zooming, rotating the display. You can use 2 fingers to zoom into a paticular point. Similarly with ctrl mouse-wheel

Inking is off by default. To ink, click the Ink checkbox for the page (in 2 page mode, there is a checkbox for each page). 
A mouse or pen or your finger can draw in red, black, or highlight. To save the ink on that page, click the Ink checkbox again.
For e.g. correcting typos on the musical staff, zoom in before inking to make it easier to draw accurately.
Ink is stored in the BMK file.

Rendering a PDF page takes time. When advancing to the next page, instantaneous response is desirable.
In single page mode, as e.g. page 5 is shown, Page 6 is prefetched and prerendered in a cache. So is page 7, and page 4. The cache does nothing if the page is already contained. 
In double page mode, for page 5, 6 being shown, 7,8 are added, as well as 4,3.
So these pages are prefetched and rendered while viewing page 5. 
For the boundary between volumes (e.g. a book contains 1000 pages, volume 1 is pages 1-100, etc.) the volumes are asynchronously read as needed.
The thumb of the slider at the top can be used to navigate the entire 1000 pages. 
The controls at the top are transparent so that the music can use more vertical screen space.

Each page has a description which is calculated from the TOC. If a song is many pages, the description is 
calculated from the closest TOC entry. If there are multiple songs on a a page, the description includes all songs on that page.

The Table of contents of a songbook shows the physical page numbers, which may not match the actual PDF page numbers (there could be a cover page scanned or could be a multivolume set, or 30 pages of intro, and then page 1 has the 1st song)
Also, each scanned page might have the physical page # printed on it.
We want to keep the scanned OCR TOC true and minimize required editing. This means the page no displayed in the UI is the same as the page # on the scanned page
PageNumberOffset will map between each so that the imported scanned TOC saved as XML will not need to be adjusted.
For 1st, 2nd, 3rd volumes, the offset from the actual scanned page number (as visible on the page) to the PDF page number
e.g. the very 1st volume might have a cover page, which is page 0 and 44 pages of intro. Viewing song "The Crush Collision March" might show page 4 on the scanned page, but it's really PdfPage=49,
so we set PageNumberOffset to -45
Another way to think about it: find a page with a printed page no on it, e.g. page 3. Count back to the 1st PDF page (probably the book cover) PageNumberOffset is the resulting count
You can edit/display the TOC via clicking or tapping the thumbnail to the right of the slider. Or Alt-E. From there you can export/import to/from the clipboard in Excel format.
You can take a screenshot of the TOC of a book, then run Optical Character Recognition (OCR) on it to convert it to text for Excel=>clipboard=>TOC.

I really love Ragtime. I suspect most people who like computer software like Ragtime. There's something so binary about it: powers of 2. 16 measures per verse, 2/4 time, syncopation.

calvin_hsia@alum.mit.edu


https://calvinhsia.visualstudio.com/DefaultCollection/MyPdfViewer/_git/MyPdfViewer


Rescan:
grinch
Piano classics 2. p161 to end  vol4, 5
Les Mis
Broadway Fake book
//the ultimate fake book  28,29    36,37  116-118   164, 165   thru phys pg 250. Pg 251 starts vol4
Ultimate Fake Book Volume II
Grest Songs of the Seventies2.pdf: rescan [63,118] 