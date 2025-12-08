using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Collections.ObjectModel;

namespace AvaloniaTests;

public partial class DataGridTestWindow : Window
{
    public DataGridTestWindow()
    {
        InitializeComponent();
        DataContext = new DataGridTestViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

public class DataGridTestViewModel
{
    public ObservableCollection<TestPerson> People { get; }

    public DataGridTestViewModel()
    {
        People = new ObservableCollection<TestPerson>
        {
            new TestPerson("Neil", "Armstrong", 39, false, "First person on the Moon"),
            new TestPerson("Buzz", "Aldrin", 39, false, "Second person on the Moon"),
            new TestPerson("Buzz", "Lightyear", 35, true, "Space Ranger from Toy Story"),
            new TestPerson("James", "Kirk", 34, true, "Captain of USS Enterprise"),
            new TestPerson("Jean-Luc", "Picard", 59, true, "Captain of USS Enterprise-D"),
            new TestPerson("Yuri", "Gagarin", 27, false, "First human in space"),
            new TestPerson("Sally", "Ride", 32, false, "First American woman in space"),
            new TestPerson("Mae", "Jemison", 36, false, "First African American woman in space"),
            new TestPerson("Ellen", "Ripley", 30, true, "Warrant Officer from Alien"),
            new TestPerson("Han", "Solo", 29, true, "Smuggler and rebel hero"),
            new TestPerson("Luke", "Skywalker", 19, true, "Jedi Knight"),
            new TestPerson("Princess", "Leia", 19, true, "Rebel Alliance leader"),
            new TestPerson("Spock", "Vulcan", 35, true, "Science Officer USS Enterprise"),
            new TestPerson("Doctor", "Who", 900, true, "Time Lord from Gallifrey"),
            new TestPerson("Rick", "Sanchez", 70, true, "Mad scientist from Rick and Morty")
        };
    }
}

public class TestPerson
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
    public bool IsFictitious { get; set; }
    public string Description { get; set; }

    public TestPerson(string firstName, string lastName, int age, bool isFictitious, string description)
    {
        FirstName = firstName;
        LastName = lastName;
        Age = age;
        IsFictitious = isFictitious;
        Description = description;
    }
}
