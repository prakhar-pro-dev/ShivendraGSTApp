using System;
using System.Windows.Forms;

namespace ShivendraGSTWindowsApp;

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        MessageBox.Show("Hello World");
    }

    private async void button1_Click(object sender, EventArgs e)
    {
        var inputPath = PathInput.Text;

        MessageBox.Show($"Entered path - {inputPath}");

        await ShivendraConsoleApp.Program.Main(inputPath);

        this.Hide();
    }

    private void textBox1_TextChanged(object sender, EventArgs e)
    {

    }
}